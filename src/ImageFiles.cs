using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
namespace AnimalTown.Updater;

internal static class ImageFiles
{
    internal static string ReadVersion(string path)
    {
        using var input=File.OpenRead(path);var header=new byte[0x440];int used=0;
        while(used<header.Length){int n=input.Read(header,used,header.Length-used);if(n==0)throw new IOException("Truncated AnimalTown image");used+=n;}
        if(header[0]!=65||header[1]!=77||header[2]!=84||header[3]!=69||header[4]!=48||header[5]!=49||header[0x1c]!=0xc2||header[0x1d]!=0x33||header[0x1e]!=0x9f||header[0x1f]!=0x3d)throw new IOException("Image is not AnimalTown");
        int end=0x20;while(end<0x420&&header[end]!=0)end++;var text=new char[end-0x20];for(int i=0;i<text.Length;i++)text[i]=(char)header[i+0x20];string title=new string(text);
        const string prefix="AnimalTown / ";if(!title.StartsWith(prefix))throw new IOException("Image has no release identity");
        string version=title.Substring(prefix.Length);ReleaseInfo.ParseVersion(version);return version;
    }
    internal static async Task<string> HashAsync(string path,CancellationToken token)
    {
        using var input=File.OpenRead(path);var bytes=new byte[16384];var hash=new Sha256();int slice=0;
        while(true){
            token.ThrowIfCancellationRequested();int n=input.Read(bytes,0,bytes.Length);if(n==0)break;hash.Append(bytes,0,n);
            // Return to the console pump during long SD verification passes.
            if((slice+=n)>=65536){slice=0;await Task.Yield();}
        }
        return hash.Finish();
    }
}

internal interface IInstallFiles
{
    bool Exists(string path);void Move(string source,string target);void Delete(string path);
    void Mark(string path);bool HasMarker(string path);
}
internal sealed class InstallFiles : IInstallFiles
{
    public bool Exists(string path)=>File.Exists(path);
    public void Move(string source,string target)=>File.Move(source,target);
    public void Delete(string path)=>File.Delete(path);
    public void Mark(string path){using var stream=File.Open(path,FileMode.CreateNew,FileAccess.Write);var bytes=new byte[]{65,84,85,80,68,65,84,69,1};stream.Write(bytes,0,bytes.Length);stream.Flush();}
    public bool HasMarker(string path){var bytes=File.ReadAllBytes(path);var expected=new byte[]{65,84,85,80,68,65,84,69,1};if(bytes.Length!=expected.Length)return false;for(int i=0;i<bytes.Length;i++)if(bytes[i]!=expected[i])return false;return true;}
}
internal static class InstallTransaction
{
    internal static void Recover(IInstallFiles files,string image)
    {
        string marker=image+".update",backup=image+".bak";
        if(!files.Exists(marker))return;
        if(!files.HasMarker(marker))throw new IOException("Unknown update marker; preserve card for recovery");
        // A cut after original -> backup must never leave the canonical name
        // missing on the next updater boot. The downloaded release is retained.
        if(!files.Exists(image)){
            if(!files.Exists(backup))throw new IOException("Update recovery needs the original backup");
            files.Move(backup,image);
        }
        files.Delete(marker);
    }
    internal static void Commit(IInstallFiles files,string image,string download)
    {
        string marker=image+".update",backup=image+".bak";
        if(image==download||!files.Exists(image)||!files.Exists(download)||files.Exists(marker))throw new IOException("Update transaction is not ready");
        files.Mark(marker);
        try{
            // The current original still exists while an older backup is retired.
            if(files.Exists(backup))files.Delete(backup);
            files.Move(image,backup);
            files.Move(download,image);
            files.Delete(marker);
        }catch{
            if(!files.Exists(image)&&files.Exists(backup))files.Move(backup,image);
            // Retain marker on failure: Recover is idempotent on the next boot.
            throw;
        }
    }
}
