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
using Directory=System.IO.Directory;
namespace AnimalTown.Updater;

internal static class Updater
{
    internal const int InstallationPhase=5;
    internal static string Operation="Read game launch request",CurrentPath="",NativeResult="";
    internal static string Status="Preparing updater";
    internal static int Phase;
    internal static long DownloadedBytes,TotalBytes;
    internal static string Artifact="",ReleaseVersion="",ReleaseSummary="";
    internal static string LaunchImage="",CleanupWarning="";
    internal static async Task<PreparedDol?> Run(CancellationToken token,Task<bool> cleanupChoice)
    {
        Operation="Read game launch request";Status="Reading game launch information";
        var request=LaunchRequest.Parse(NativeLaunchArguments.Read());
        await Task.Yield();token.ThrowIfCancellationRequested();
        Operation="Read launched ISO version";CurrentPath=IsoVersion.DiscPath;Status="Reading the running release version";
        if(!Storage.IsMounted(StorageDevice.DvdDrive)&&!Storage.MountDisc())throw new IOException("Could not mount the launched AnimalTown disc");
        string installedVersion;
        using(var version=File.OpenRead(IsoVersion.DiscPath))installedVersion=IsoVersion.Read(version);
        request.VerifyInstalledVersion(installedVersion);
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
        // Validate and stage the helper before configuring HTTPS or downloading.
        // Its native image stays resident through the handoff.
        Operation="Prepare Swiss launch helper";CurrentPath="gcloader:/";Status="Finding Swiss on the GC Loader drive root";
        var swiss=await SwissDiscovery.FindAsync("gcloader:/",token,static path=>CurrentPath=path);
        string swissPath=swiss.Path;CurrentPath=swissPath;Status="Preparing Swiss r"+swiss.Revision;
        PreparedDol? launch=null;
        try{
            launch=await DolLauncher.PrepareAsync(swissPath,token);
            Phase=1;Operation="Start Broadband Adapter";CurrentPath="";Status="Starting Broadband Adapter";
            await NetworkRuntime.StartAsync(new NetworkOptions(),token);await ConfigureTls(token);
            string target="";
            using(var http=new HttpClient()){
                http.MaximumResponseBytes=ReleaseInfo.MaximumMetadataBytes;http.MaximumStreamBytes=int.MaxValue;http.StreamReadBufferSize=16384;
                http.DefaultRequestHeaders.Add("User-Agent","AnimalTown-Updater/5");http.DefaultRequestHeaders.Add("Accept","application/vnd.github+json");
                http.DefaultRequestHeaders.Add("X-GitHub-Api-Version","2022-11-28");
                Phase=2;Status="Checking the latest numbered release";
                var release=await ReadStableRelease(http,token);
                if(ReleaseInfo.NeedsStableUpdate(installedVersion,release.Version)){
                    ReleaseVersion=release.Version;ReleaseSummary=release.Summary;
                    Artifact=release.Name;TotalBytes=release.Size;target=request.DownloadPath(release.Name);
                    Directory.CreateDirectory(LaunchRequest.GamesDirectory);
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
                }else{
                    Status="No newer stable release. Running "+installedVersion;
                }
            }

            // Close HTTP/TLS and stop the network before the irreversible step.
            Operation="Stop updater network";CurrentPath=target;TlsConfiguration.Reset();NetworkRuntime.Stop();
            token.ThrowIfCancellationRequested();
            if(target.Length==0){
                launch.Dispose();launch=null;Phase=2;Operation="Release check complete";CurrentPath=IsoVersion.DiscPath;return null;
            }
            Operation="Prepare new image autoload";launch.SetArguments(SwissLaunch.Arguments(swissPath,target));
            Operation="Select and verify GC Loader image";CurrentPath=target;Status="Selecting and verifying the game disc";Phase=InstallationPhase;
            bool cleanupEnabled=await cleanupChoice;
            token.ThrowIfCancellationRequested();
            Storage.SelectGcLoaderDiscImage(target);
            if(cleanupEnabled){
                Operation="Remove older AnimalTown copies";Status="Removing older AnimalTown copies";
                try{
                    await StableImageDiscovery.RemoveOlderCopiesAsync(request.Root,target,ReleaseVersion,static path=>CurrentPath=path);
                }catch(Exception error){
                    // Cleanup can fail after the new disc has been selected.
                    // Keep the prepared Swiss handoff available from the warning screen.
                    string message=string.IsNullOrEmpty(error.Message)?"Could not remove all older copies.":error.Message;
                    CleanupWarning=message+"\n"+CurrentPath;
                }
            }
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
        token.ThrowIfCancellationRequested();Operation="Read bounded stable release metadata";
        CurrentPath=ReleaseInfo.LatestMetadataUrl;
        using var metadata=await http.GetAsync(CurrentPath,HttpCompletionOption.ResponseContentRead,token);
        metadata.EnsureSuccessStatusCode();
        return ReleaseInfo.ParseReleaseMetadata(await metadata.Content.ReadAsStringAsync());
    }
    private static async Task Download(HttpClient http,ReleaseInfo release,string destination,CancellationToken token)
    {
        Phase=3;Operation="Download stable release";CurrentPath=destination;DownloadedBytes=0;TotalBytes=release.Size;Status="Downloading release image";
        using var response=await http.GetAsync(release.Url,HttpCompletionOption.ResponseHeadersRead,token);response.EnsureSuccessStatusCode();
        // GitHub metadata bounds the reservation. No ISO-sized managed allocation.
        using var body=await response.Content.ReadAsStreamAsync();
        using var output=Storage.CreateContiguousFile(destination,release.Size);
        await DownloadTransfer.CopyAsync(body,block=>output.Write(block.Buffer,0,block.Count),release.Size,release.Digest,
            static written=>DownloadedBytes=written,token);
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
