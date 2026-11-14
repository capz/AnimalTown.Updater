using System;
using System.IO;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace AnimalTown.Updater;

internal sealed class StableImage
{
    internal readonly string Path,Version,Directory;
    internal StableImage(string path,string version)
    {
        Path=path;Version=version;int slash=path.Length-1;while(slash>=0&&path[slash]!='/')slash--;
        Directory=path.Substring(0,slash+1);
    }
    internal string DownloadPath(string fileName)
    {
        if(fileName==null||fileName.IndexOf('/')>=0||fileName.IndexOf('\\')>=0||!fileName.EndsWith(".nkit.iso")||
           !StableImageDiscovery.TryFileVersion(fileName,out _))throw new IOException("Invalid stable release image filename");
        string path=Directory+fileName;
        if(path.Length>900)throw new IOException("Release image path is too long");return path;
    }
}

internal static class StableImageDiscovery
{
    internal static bool TryFileVersion(string path,out string version)
    {
        version="";if(path==null)return false;string name=System.IO.Path.GetFileName(path);
        const string prefix="AnimalTown-";
        if(name.Length<=prefix.Length+4||!SamePrefix(name,prefix))return false;
        int suffix=SameSuffix(name,".nkit.iso")?9:SameSuffix(name,".iso")?4:0;
        if(suffix==0)return false;
        string candidate=name.Substring(prefix.Length,name.Length-prefix.Length-suffix);
        try{ReleaseInfo.ParseStableVersion(candidate);}catch(FormatException){return false;}
        version=candidate;return true;
    }
    private static bool SamePrefix(string value,string prefix)
    {
        for(int i=0;i<prefix.Length;i++)if(Lower(value[i])!=Lower(prefix[i]))return false;return true;
    }
    private static bool SameSuffix(string value,string suffix)=>value.Length>=suffix.Length&&SamePrefix(value.Substring(value.Length-suffix.Length),suffix);
    private static bool IsStagingPath(string path)
    {
        const string component=".animaltown-update";
        int start=0;
        for(int i=0;i<=path.Length;i++)if(i==path.Length||path[i]=='/'){
            if(i-start==component.Length&&SamePrefix(path.Substring(start,i-start),component))return true;
            start=i+1;
        }
        return false;
    }
    private static char Lower(char value)=>value>='A'&&value<='Z'?(char)(value+32):value;
    internal static int CompareVersions(string a,string b)
    {
        int[] x=ReleaseInfo.ParseStableVersion(a),y=ReleaseInfo.ParseStableVersion(b);
        for(int i=0;i<3;i++)if(x[i]!=y[i])return x[i]<y[i]?-1:1;return 0;
    }
    internal static int ComparePaths(string a,string b)
    {
        int length=Math.Min(a.Length,b.Length);
        for(int i=0;i<length;i++){char x=Lower(a[i]),y=Lower(b[i]);if(x!=y)return x<y?-1:1;}
        return a.Length==b.Length?0:a.Length<b.Length?-1:1;
    }
    internal static async Task<StableImage> ScanAsync(string root,CancellationToken token,Action<string>? reportPath=null)
    {
        StableImage? best=null;int work=0,depth=0;
        // A top-directory cursor does not collect sibling paths. Bound active
        // native directory handles and keep one of the SDK's 32 slots available
        // to read an image header. Overflow aborts, never selects a partial scan.
        var cursors=new IEnumerator<string>?[31];
        var directories=new string?[cursors.Length];directories[0]=root;
        token.ThrowIfCancellationRequested();
        reportPath?.Invoke(root);
        cursors[0]=System.IO.Directory.EnumerateFileSystemEntries(root).GetEnumerator();
        try{
            while(depth>=0){
                token.ThrowIfCancellationRequested();var cursor=cursors[depth]!;
                // Native enumeration also inspects each entry. Keep the
                // containing directory visible if that filesystem call fails.
                reportPath?.Invoke(directories[depth]!);
                if(!cursor.MoveNext()){cursor.Dispose();cursors[depth]=null;directories[depth]=null;depth--;continue;}
                string path=cursor.Current;
                reportPath?.Invoke(path);
                if(++work>=32){work=0;await Task.Yield();}
                if(IsStagingPath(path))continue;
                if(System.IO.Directory.Exists(path)){
                    if(depth+1==cursors.Length)throw new IOException("GC Loader directory nesting exceeds the bounded scan depth; no image changed");
                    directories[++depth]=path;
                    cursors[depth]=System.IO.Directory.EnumerateFileSystemEntries(path).GetEnumerator();continue;
                }
                if(!TryFileVersion(path,out string version))continue;
                // Real read failures abort; bad/foreign headers are rejected.
                if(!ImageFiles.TryReadVersion(path,out string actual)||actual!=version)continue;
                if(best==null||CompareVersions(version,best.Version)>0||CompareVersions(version,best.Version)==0&&ComparePaths(path,best.Path)<0)
                    best=new StableImage(path,version);
            }
        }finally{
            for(int i=0;i<cursors.Length;i++)cursors[i]?.Dispose();
        }
        token.ThrowIfCancellationRequested();return best??throw new IOException("No stable AnimalTown ISO was found on the GC Loader volume");
    }
}
