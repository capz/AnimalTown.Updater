using System;
using System.IO;

namespace AnimalTown.Updater;

// v3 reads the launched disc's version.md. Older launch envelopes remain
// readable, but their paths never select an image for replacement or deletion.
internal sealed class LaunchRequest
{
    internal const string Protocol="--animaltown-update=1";
    internal const string DiscoveryProtocol="--animaltown-update=2";
    internal const string DiscVersionProtocol="--animaltown-update=3";
    internal readonly string Root="gcloader:/";
    internal readonly string Image,Version,Directory,Backup,Marker;

    private LaunchRequest(string image,string version)
    {
        Image=image;Version=version;
        if(image.Length==0){Directory=Root;Backup=Marker="";return;}
        int slash=image.Length-1;while(image[slash]!='/')slash--;Directory=image.Substring(0,slash+1);
        Backup=image+".bak";Marker=image+".update";
    }

    internal static LaunchRequest Parse(string[] args)
    {
        if(args!=null&&args.Length==3&&(args[1]==DiscoveryProtocol||args[1]==DiscVersionProtocol)){
            ValidateLauncher(args[0]);ReleaseInfo.ParseVersion(args[2]);return new LaunchRequest("",args[2]);
        }
        if(args==null||args.Length!=4||args[0]==null||args[0].Length==0||args[1]!=Protocol)
            throw new IOException("Launch the updater from AnimalTown with its running version.");
        // argv[0] identifies the updater DOL, which can be embedded on dvd: or
        // renamed on SD. It is never interpreted as the image to overwrite.
        ValidateLauncher(args[0]);
        string image=args[2],version=args[3];ValidateImage(image);ReleaseInfo.ParseVersion(version);
        return new LaunchRequest(image,version);
    }
    private static void ValidateLauncher(string path)
    {
        if(path==null||path.Length==0)throw new IOException("Missing updater launch path");
        Utf8Length(path,900);for(int i=0;i<path.Length;i++)if(path[i]<32||path[i]==127)throw new IOException("Invalid updater launch path.");
    }

    private static void ValidateImage(string image)
    {
        const string root="gcloader:/";
        if(image==null||!image.StartsWith(root)||image.Length<root.Length+5)throw new IOException("Update target must be a full GC Loader image path.");
        Utf8Length(image,900);int component=root.Length;
        for(int i=component;i<=image.Length;i++){
            if(i<image.Length){
                char c=image[i];
                if(c<32||c>=127&&c<=159||c=='\\'||c==':'||c=='='||c=='*'||c=='?'||c=='"'||c=='<'||c=='>'||c=='|')throw new IOException("Invalid update image path.");
                if(c!='/')continue;
            }
            int length=i-component;
            if(length==0||length>255||length==1&&image[component]=='.'||length==2&&image[component]=='.'&&image[component+1]=='.'||image[i-1]==' '||image[i-1]=='.')
                throw new IOException("Use a canonical update image path.");
            if(i==image.Length&&length>248)throw new IOException("Image filename leaves no room for recovery files.");
            component=i+1;
        }
        string suffix=image.Substring(image.Length-4).ToLowerInvariant();
        if(suffix!=".iso"&&suffix!=".gcm")throw new IOException("Update target must be an ISO or GCM image.");
    }

    private static int Utf8Length(string text,int maximum)
    {
        int bytes=0;
        for(int i=0;i<text.Length;i++){
            int c=text[i];
            if(c>=0xd800&&c<=0xdbff){
                if(++i>=text.Length||text[i]<0xdc00||text[i]>0xdfff)throw new IOException("Invalid UTF-16 in launch path.");
                bytes+=4;
            }else if(c>=0xdc00&&c<=0xdfff)throw new IOException("Invalid UTF-16 in launch path.");
            else bytes+=c<128?1:c<2048?2:3;
            if(bytes>maximum)throw new IOException("Update path is too long.");
        }
        return bytes;
    }

    internal static string DownloadPath(string gamesDirectory,string fileName)
    {
        const string prefix="AnimalTown-",suffix=".nkit.iso";
        if(fileName==null||!fileName.StartsWith(prefix)||!fileName.EndsWith(suffix)||fileName.Length<=prefix.Length+suffix.Length)
            throw new IOException("Invalid release image filename.");
        ReleaseInfo.ParseVersion(fileName.Substring(prefix.Length,fileName.Length-prefix.Length-suffix.Length));
        string result=gamesDirectory+fileName;Utf8Length(result,1000);return result;
    }

    internal void VerifyInstalledVersion(string actual)
    {
        ReleaseInfo.ParseVersion(actual);
        if(actual!=Version)throw new IOException("The image version differs from the running game. Reload before updating.");
    }
}
