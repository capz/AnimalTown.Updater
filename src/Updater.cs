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
using Dolphin.Security;
using File=System.IO.File;
using Path=System.IO.Path;
namespace AnimalTown.Updater;

internal static class Updater
{
    internal static string Operation="Read game launch request",CurrentPath="",NativeResult="";
    internal static string Status="Preparing updater";
    internal static int Phase;
    internal static long DownloadedBytes,TotalBytes;
    internal static string Artifact="";
    internal static string LaunchImage="";
    internal static async Task<PreparedDol> Run(CancellationToken token)
    {
        Operation="Read game launch request";Status="Reading game launch information";
        LaunchRequest.Parse(NativeLaunchArguments.Read());
        await Task.Yield();token.ThrowIfCancellationRequested();
        // The updater and its console are resident in MEM1. Never touch an
        // image while dvd:/ still owns its source volume.
        Operation="Release source disc";CurrentPath="dvd:/";Status="Releasing the game disc";
        Storage.Unmount(StorageDevice.DvdDrive);
        if(Storage.IsMounted(StorageDevice.DvdDrive))throw new IOException("Could not release the updater's source disc");
        // One process-wide scope covers updater-owned writable SD I/O. Reads
        // remain read-only; downloads still require the strict contiguous writer.
        using var allocationPolicy=Defrag.BeginScope(true,65536);
        Operation="Mount writable GC Loader volume";CurrentPath="gcloader:/";Status="Mounting GC Loader SD card";
        await Task.Yield();token.ThrowIfCancellationRequested();
        if(!Storage.IsMounted(StorageDevice.GcLoader)&&!Storage.Mount(StorageDevice.GcLoader)){NativeResult="GC Loader mount returned false";throw new IOException("GC Loader SD is unavailable or not writable");}
        Operation="Find highest stable installation";Status="Searching GC Loader for stable AnimalTown images";
        var selected=await StableImageDiscovery.ScanAsync("gcloader:/",token,static path=>CurrentPath=path);
        CurrentPath=selected.Path;Status="Selected "+selected.Version+" in "+selected.Directory;
        await Task.Yield();token.ThrowIfCancellationRequested();
        // Validate and stage the helper before configuring HTTPS or downloading.
        // Its native image stays resident through the handoff.
        Operation="Prepare Swiss launch helper";CurrentPath="gcloader:/";Status="Finding Swiss on the GC Loader drive root";
        var swiss=await SwissDiscovery.FindAsync("gcloader:/",token,static path=>CurrentPath=path);
        string swissPath=swiss.Path;CurrentPath=swissPath;Status="Preparing Swiss r"+swiss.Revision;
        PreparedDol? launch=null;
        try{
            launch=await DolLauncher.PrepareAsync(swissPath,token);
            launch.SetArguments(SwissLaunch.Arguments(swissPath,selected.Path));
            Phase=1;Operation="Start Broadband Adapter";CurrentPath="";Status="Starting Broadband Adapter";
            await NetworkRuntime.StartAsync(new NetworkOptions(),token);await ConfigureTls(token);
            string target=selected.Path;
            using(var http=new HttpClient()){
                http.MaximumResponseBytes=ReleaseInfo.MaximumFeedBytes;http.MaximumStreamBytes=int.MaxValue;
                http.DefaultRequestHeaders.Add("User-Agent","AnimalTown-Updater/3");http.DefaultRequestHeaders.Add("Accept","*/*");
                Phase=2;Status="Checking the latest numbered release";
                var release=await ReadStableRelease(http,token);
                if(ReleaseInfo.NeedsStableUpdate(selected.Version,release.Version)){
                    Artifact=release.Name;TotalBytes=release.Size;target=selected.DownloadPath(release.Name);
                    CurrentPath=target;
                    // Existing files are never truncated or silently replaced.
                    // A complete previous download can be reused only after
                    // checking the exact release digest and embedded version.
                    if(!File.Exists(target))await Download(http,release,target,token);
                    Phase=4;Operation="Verify published release image";CurrentPath=target;Status="Verifying release image";
                    using(var check=File.OpenRead(target))if(check.Length!=release.Size)throw new IOException("Release length does not match; original ISO preserved");
                    if(await ImageFiles.HashAsync(target,token)!=release.Digest||ImageFiles.ReadVersion(target)!=release.Version)
                        throw new IOException("Release verification failed; original ISO preserved");
                    DownloadedBytes=TotalBytes;
                }else Status="No newer stable release; starting "+selected.Version;
            }

            // Close HTTP/TLS and stop the network before the irreversible step.
            Operation="Stop updater network";CurrentPath=target;TlsConfiguration.Reset();NetworkRuntime.Stop();
            token.ThrowIfCancellationRequested();
            Operation="Prepare new image autoload";launch.SetArguments(SwissLaunch.Arguments(swissPath,target));
            Operation="Revalidate selected installation";CurrentPath=selected.Path;
            if(ImageFiles.ReadVersion(selected.Path)!=selected.Version)throw new IOException("The selected installation changed during the update");
            bool replacing=StableImageDiscovery.ComparePaths(selected.Path,target)!=0;
            if(replacing){
                Phase=5;Operation="Select and verify GC Loader image";CurrentPath=target;Status="Selecting and verifying the game disc";
                await Task.Yield();
            }
            token.ThrowIfCancellationRequested();
            InstallTransaction.ActivateAndDeleteSelected(new InstallFiles(),selected.Path,target,path=>{
                // This changes GC Loader's hardware map and compares physical
                // DVD reads with the verified file. Never remount the raw
                // volume afterwards: its startup routine resets the drive.
                Storage.SelectGcLoaderDiscImage(path);
                Operation="Delete selected old installation";CurrentPath=selected.Path;Status="Finishing the verified installation";
            });
            LaunchImage=target;Phase=6;Operation="Launch selected image through Swiss";CurrentPath=target;Status="Starting AnimalTown";
            var prepared=launch;launch=null;return prepared;
        }catch{
            launch?.Dispose();
            try{TlsConfiguration.Reset();NetworkRuntime.Stop();}catch{ }
            throw;
        }
    }
    private static async Task<ReleaseInfo> ReadStableRelease(HttpClient http,CancellationToken token)
    {
        token.ThrowIfCancellationRequested();Operation="Read compact stable update feed";
        CurrentPath=ReleaseInfo.LatestFeedUrl;
        using var metadata=await http.GetAsync(CurrentPath,HttpCompletionOption.ResponseContentRead,token);
        metadata.EnsureSuccessStatusCode();
        return ReleaseInfo.ParseUpdateFeed(await metadata.Content.ReadAsStringAsync());
    }
    private static async Task Download(HttpClient http,ReleaseInfo release,string destination,CancellationToken token)
    {
        Phase=3;Operation="Download stable release";CurrentPath=destination;DownloadedBytes=0;TotalBytes=release.Size;Status="Downloading release image";
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
    internal static async Task ConfigureTls(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        Operation="Check console clock";CurrentPath="GameCube clock";Status="Checking console clock";
        UpdaterClock.LocalUnixSeconds(SystemClock.Read());
        Operation="Collect console entropy";CurrentPath="GameCube entropy provider";Status="Preparing secure connection";
        byte[] entropy=await PlatformEntropy.GetBytesAsync(48,token);
        byte[]? roots=null;
        try{
            token.ThrowIfCancellationRequested();
            Operation="Initialize embedded HTTPS trust";CurrentPath="Embedded CA certificates";Status="Configuring HTTPS";
            roots=EmbeddedTrust.LoadRoots();
            TlsConfiguration.InitializeFromLocalClock(roots,entropy,UpdaterClock.LocalUnixSeconds(SystemClock.Read()));
        }finally{
            Array.Clear(entropy,0,entropy.Length);
            if(roots!=null)Array.Clear(roots,0,roots.Length);
        }
    }
}
