using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Dolphin.IO;
using Dolphin.Network;
using HttpClient=Dolphin.Network.Http.HttpClient;
using HttpCompletionOption=Dolphin.Network.Http.HttpCompletionOption;
using Dolphin.Storage;
using Dolphin.Timing;
using File=System.IO.File;
using Path=System.IO.Path;
namespace AnimalTown.Updater;

internal static class Updater
{
    internal const string Settings="gcloader:/animaltown-updater/";
    internal static string Operation="Read game launch request",CurrentPath="",NativeResult="";
    internal static string Status="Preparing updater";
    internal static int Phase;
    internal static long DownloadedBytes,TotalBytes;
    internal static string Artifact="";
    internal static async Task Run(CancellationToken token)
    {
        Operation="Read game launch request";Status="Reading game launch information";
        var request=LaunchRequest.Parse(NativeLaunchArguments.Read());CurrentPath=request.Image;
        await Task.Yield();token.ThrowIfCancellationRequested();
        // The DOL is staged in MEM1. Release the source disc before recovery
        // can rename its backing image; no updater asset depends on dvd:/.
        Operation="Release source disc";CurrentPath="dvd:/";Status="Releasing the game disc";
        Storage.Unmount(StorageDevice.DvdDrive);
        if(Storage.IsMounted(StorageDevice.DvdDrive))throw new IOException("Could not release the updater's source disc");
        Operation="Mount writable GC Loader volume";CurrentPath=request.Image;Status="Mounting GC Loader SD card";
        await Task.Yield();token.ThrowIfCancellationRequested();
        if(!Storage.Mount(StorageDevice.GcLoader)){NativeResult="GC Loader mount returned false";throw new IOException("GC Loader SD is unavailable or not writable");}
        var files=new InstallFiles();
        Operation="Verify installed image before recovery";CurrentPath=request.Image;Status="Checking the installed game image";
        await Task.Yield();
        string original=request.Image;
        if(!files.Exists(original)&&files.Exists(request.Marker)&&files.HasMarker(request.Marker)&&files.Exists(request.Backup))original=request.Backup;
        CurrentPath=original;request.VerifyInstalledVersion(ImageFiles.ReadVersion(original));
        Operation="Recover interrupted installation";CurrentPath=request.Image;
        InstallTransaction.Recover(files,request.Image);
        Operation="Read installed image header";
        string local=ImageFiles.ReadVersion(request.Image);request.VerifyInstalledVersion(local);
        Phase=1;Operation="Start Broadband Adapter";CurrentPath="";Status="Starting Broadband Adapter";await NetworkRuntime.StartAsync(new NetworkOptions(),token);
        ConfigureTls();
        using(var http=new HttpClient()){
            http.MaximumStreamBytes=int.MaxValue;
            http.DefaultRequestHeaders.Add("User-Agent","AnimalTown-Updater/1");http.DefaultRequestHeaders.Add("Accept","application/vnd.github+json");
            http.DefaultRequestHeaders.Add("X-GitHub-Api-Version","2022-11-28");
            Phase=2;Operation="Read latest GitHub release";CurrentPath="https://api.github.com/repos/capz/AnimalTown/releases";Status="Checking GitHub releases";
            using(var metadata=await http.GetAsync("https://api.github.com/repos/capz/AnimalTown/releases?per_page=1",HttpCompletionOption.ResponseContentRead,token)){
                metadata.EnsureSuccessStatusCode();var release=ReleaseInfo.Parse(await metadata.Content.ReadAsStringAsync());
                if(ReleaseInfo.NeedsUpdate(local,release.Version)){
                    Artifact=release.Name;TotalBytes=release.Size;
                    string download=request.DownloadPath(release.Name);
                    Operation="Create image staging directory";CurrentPath=request.DownloadDirectory;System.IO.Directory.CreateDirectory(request.DownloadDirectory);
                    if(!File.Exists(download))await Download(http,release,download,token);
                    Phase=4;Operation="Verify downloaded image";CurrentPath=download;Status="Verifying release image";
                    if(await ImageFiles.HashAsync(download,token)!=release.Digest||ImageFiles.ReadVersion(download)!=release.Version)throw new IOException("Release verification failed; original ISO preserved");
                    DownloadedBytes=TotalBytes;
                    token.ThrowIfCancellationRequested();Phase=5;Status="Installing "+release.Version;
                    Operation="Verify installed image before replacement";CurrentPath=request.Image;request.VerifyInstalledVersion(ImageFiles.ReadVersion(request.Image));
                    Operation="Install verified release";InstallTransaction.Commit(files,request.Image,download);
                }else Status="Already up to date: "+local;
            }
        }
        TlsConfiguration.Reset();NetworkRuntime.Stop();
        Phase=6;Operation="Release updated data volume";CurrentPath=request.Image;Status="Update complete. Power off and on to start AnimalTown.";
    }
    private static async Task Download(HttpClient http,ReleaseInfo release,string destination,CancellationToken token)
    {
        Phase=3;Operation="Download verified release to staging";CurrentPath=destination;DownloadedBytes=0;TotalBytes=release.Size;Status="Downloading release image";
        using var response=await http.GetAsync(release.Url,HttpCompletionOption.ResponseHeadersRead,token);response.EnsureSuccessStatusCode();
        // GitHub metadata bounds the reservation. No ISO-sized managed allocation.
        using var body=await response.Content.ReadAsStreamAsync();
        using var output=Storage.CreateContiguousFile(destination,release.Size);
        var hash=new Sha256();var buffer=new byte[16384];int total=0;
        while(true){
            int count=await body.ReadAsync(buffer,0,buffer.Length,token);if(count==0)break;
            if(count>release.Size-total)throw new IOException("Download exceeds release length");
            output.Write(buffer,0,count);hash.Append(buffer,0,count);total+=count;DownloadedBytes=total;
        }
        if(total!=release.Size||hash.Finish()!=release.Digest)throw new IOException("Incomplete or damaged release; original ISO preserved");
        // Complete verifies the actual card bytes and contiguous cluster chain,
        // then publishes the unmodified GitHub filename.
        Phase=4;Operation="Verify staged SD card bytes";CurrentPath=destination;Status="Verifying SD card write";await output.CompleteAsync(token);
    }
    private static void ConfigureTls()
    {
        Operation="Read HTTPS trust and fresh entropy";CurrentPath=Settings;Status="Configuring HTTPS";
        byte[] roots=File.ReadAllBytes(Settings+"roots.pem"),entropy=File.ReadAllBytes(Settings+"entropy.bin");
        try{
            if(entropy.Length<48||entropy.Length>64)throw new IOException("Provision fresh updater entropy on a trusted host");
            int offset=int.Parse(File.ReadAllText(Settings+"utc-offset-minutes.txt"));if(offset<-840||offset>840)throw new IOException("Invalid console UTC offset");
            var clock=SystemClock.Read();int days=0;for(int year=2000;year<clock.Year;year++)days+=year%4==0?366:365;
            for(int month=1;month<clock.Month;month++)days+=CalendarTime.DaysInMonth(clock.Year,month);
            long utc=946684800L+(days+clock.Day-1)*86400L+clock.Hour*3600+clock.Minute*60+clock.Second-offset*60;
            // Delete before use. A failed handshake still consumes this private
            // one-shot seed; it is never embedded in a public release or reused.
            File.Delete(Settings+"entropy.bin");TlsConfiguration.Initialize(roots,entropy,utc);
        }finally{Array.Clear(entropy,0,entropy.Length);}
    }
}
