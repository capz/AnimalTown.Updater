using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace AnimalTown.Updater;

internal sealed class SwissImage
{
    internal readonly string Path;
    internal readonly int Revision;
    internal SwissImage(string path,int revision){Path=path;Revision=revision;}
}

internal static class SwissDiscovery
{
    internal const int MaximumBytes=8*1024*1024;
    internal static bool IsDolPath(string path)
    {
        int n=path.Length;
        return n>=4&&path[n-4]=='.'&&(path[n-3]=='d'||path[n-3]=='D')&&
            (path[n-2]=='o'||path[n-2]=='O')&&(path[n-1]=='l'||path[n-1]=='L');
    }
    internal static async Task<SwissImage> FindAsync(string root,CancellationToken token,Action<string>? reportPath=null,SwissBuild[]? builds=null)
    {
        builds=builds??SwissBuilds.Official;
        SwissImage? best=null;byte[]? buffer=null;int work=0;
        token.ThrowIfCancellationRequested();reportPath?.Invoke(root);
        // Do not recurse or collect names. Existing root-level boot.dol and
        // versioned Swiss names work without trusting a filename as identity.
        foreach(string path in Directory.EnumerateFiles(root)){
            token.ThrowIfCancellationRequested();reportPath?.Invoke(path);
            if(++work>=32){work=0;await Task.Yield();}
            if(!IsDolPath(path))continue;
            buffer=buffer??new byte[16384];
            var build=await ProbeAsync(path,builds,buffer,token);
            if(build!=null&&(best==null||build.Revision>best.Revision||
                build.Revision==best.Revision&&StableImageDiscovery.ComparePaths(path,best.Path)<0))
                best=new SwissImage(path,build.Revision);
        }
        if(best!=null){token.ThrowIfCancellationRequested();return best;}
        // Preserve an already-provisioned helper, with the same content checks,
        // only when the root contains no supported Swiss executable.
        string legacy=System.IO.Path.Combine(root,"animaltown-updater/swiss.dol");
        reportPath?.Invoke(legacy);token.ThrowIfCancellationRequested();
        if(File.Exists(legacy)){
            buffer=buffer??new byte[16384];
            var build=await ProbeAsync(legacy,builds,buffer,token);
            if(build!=null)return new SwissImage(legacy,build.Revision);
        }
        throw new IOException("No supported Swiss DOL found. Copy an official Swiss normal DOL to the GC Loader drive root.");
    }
    private static bool KnownLength(long length,SwissBuild[] builds)
    {
        if(length<256||length>MaximumBytes)return false;
        for(int i=0;i<builds.Length;i++)if(builds[i].Length==length)return true;
        return false;
    }
    private static async Task<SwissBuild?> ProbeAsync(string path,SwissBuild[] builds,byte[] buffer,CancellationToken token)
    {
        using var input=File.OpenRead(path);long length=input.Length;
        if(!KnownLength(length,builds)||!ReadExact(input,buffer,256)||!EntryOffset(buffer,(uint)length,out int entry))return null;
        input.Seek(entry,SeekOrigin.Begin);
        if(!ReadExact(input,buffer,32)||Word(buffer,4)!=0x5f617267)return null;
        input.Seek(0,SeekOrigin.Begin);var hash=new Sha256();long used=0;int slice=0;
        while(true){
            token.ThrowIfCancellationRequested();int count=input.Read(buffer,0,buffer.Length);
            if(count==0)break;
            used+=count;if(used>length)return null;hash.Append(buffer,0,count);
            if((slice+=count)>=65536){slice=0;await Task.Yield();}
        }
        if(used!=length||input.Length!=length)return null;
        string digest=hash.Finish();
        for(int i=0;i<builds.Length;i++)if(builds[i].Length==length&&builds[i].Digest==digest)return builds[i];
        return null;
    }
    private static bool ReadExact(FileStream input,byte[] buffer,int count)
    {
        int used=0;while(used<count){int n=input.Read(buffer,used,count-used);if(n==0)return false;used+=n;}return true;
    }
    private static uint Word(byte[] data,int at)=>(uint)data[at]<<24|(uint)data[at+1]<<16|(uint)data[at+2]<<8|data[at+3];
    private static bool MemoryRange(uint address,uint size)=>address>=0x80003100&&address<0x81800000&&size<=0x81800000-address;
    private static bool Overlap(uint a,uint n,uint b,uint m)=>n!=0&&m!=0&&(a<=b?b-a<n:a-b<m);
    // A cheap bounded prefilter. Native staging and SetArguments still perform
    // authoritative layout/relocation/argv validation on the selected image.
    private static bool EntryOffset(byte[] header,uint length,out int entryOffset)
    {
        entryOffset=-1;uint entry=Word(header,0xe0),bss=Word(header,0xd8),bssSize=Word(header,0xdc);
        if((entry&3)!=0||bssSize!=0&&!MemoryRange(bss,bssSize))return false;
        for(int i=0;i<18;i++){
            uint offset=Word(header,i*4),address=Word(header,0x48+i*4),size=Word(header,0x90+i*4);
            if(size==0)continue;
            if(offset<256||offset>length||size>length-offset||(address&3)!=0||!MemoryRange(address,size))return false;
            for(int j=0;j<i;j++){
                uint otherSize=Word(header,0x90+j*4);
                if(Overlap(offset,size,Word(header,j*4),otherSize)||Overlap(address,size,Word(header,0x48+j*4),otherSize))return false;
            }
            if(i<7&&size>=32&&entry>=address&&entry-address<=size-32)entryOffset=(int)(offset+entry-address);
        }
        return entryOffset>=0;
    }
}
