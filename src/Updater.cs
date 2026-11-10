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
    internal const string Root="gcloader:/",Image=Root+"AnimalTown.iso",Settings=Root+"animaltown-updater/";
    internal static string Status="Preparing updater";
    internal static int Progress;
    internal static async Task<PreparedDol> Run(CancellationToken token)
    {
        Status="Mounting GC Loader SD card";
        if(!Storage.Mount(StorageDevice.GcLoader))throw new IOException("GC Loader SD is unavailable or not writable");
        var files=new InstallFiles();InstallTransaction.Recover(files,Image);
        string local=ImageFiles.ReadVersion(Image);
        // Validate and stage the complete boot path before network I/O or renames.
        var boot=DiscLauncher.PrepareWithSwiss(Root+"swiss.dol",Image);
        try{
            Status="Starting Broadband Adapter";await NetworkRuntime.StartAsync(new NetworkOptions(),token);
            ConfigureTls();
            using(var http=new HttpClient()){
                http.MaximumStreamBytes=int.MaxValue;
                http.DefaultRequestHeaders.Add("User-Agent","AnimalTown-Updater/1");http.DefaultRequestHeaders.Add("Accept","application/vnd.github+json");
                http.DefaultRequestHeaders.Add("X-GitHub-Api-Version","2022-11-28");
                Status="Checking GitHub releases (including development builds)";
                using(var metadata=await http.GetAsync("https://api.github.com/repos/capz/AnimalTown/releases?per_page=1",HttpCompletionOption.ResponseContentRead,token)){
                    metadata.EnsureSuccessStatusCode();var release=ReleaseInfo.Parse(await metadata.Content.ReadAsStringAsync());
                    if(ReleaseInfo.IsNewer(local,release.Version)){
                        string download=Root+release.Name;
                        if(!File.Exists(download))await Download(http,release,download,token);
                        Status="Verifying downloaded ISO";
                        if(ImageFiles.Hash(download)!=release.Digest||ImageFiles.ReadVersion(download)!=release.Version)throw new IOException("Release verification failed; original ISO preserved");
                        token.ThrowIfCancellationRequested();Status="Installing "+release.Version;
                        InstallTransaction.Commit(files,Image,download);
                    }else Status="Already up to date: "+local;
                }
            }
            TlsConfiguration.Reset();NetworkRuntime.Stop();
            Status="Launching AnimalTown";Progress=100;return boot;
        }catch{boot.Dispose();throw;}
    }
    private static async Task Download(HttpClient http,ReleaseInfo release,string destination,CancellationToken token)
    {
        using var response=await http.GetAsync(release.Url,HttpCompletionOption.ResponseHeadersRead,token);response.EnsureSuccessStatusCode();
        // GitHub metadata bounds the reservation. No ISO-sized managed allocation.
        using var body=await response.Content.ReadAsStreamAsync();
        using var output=Storage.CreateContiguousFile(destination,release.Size);
        var hash=new Sha256();var buffer=new byte[16384];int total=0,last=-1;
        while(true){
            int count=await body.ReadAsync(buffer,0,buffer.Length,token);if(count==0)break;
            if(count>release.Size-total)throw new IOException("Download exceeds release length");
            output.Write(buffer,0,count);hash.Append(buffer,0,count);total+=count;Progress=(int)(total*100L/release.Size);
            if(Progress!=last){last=Progress;Status="Downloading "+release.Name+" / "+Progress+"%";}
        }
        if(total!=release.Size||hash.Finish()!=release.Digest)throw new IOException("Incomplete or damaged release; original ISO preserved");
        // Complete verifies the actual card bytes and contiguous cluster chain,
        // then publishes the unmodified GitHub filename.
        Status="Checking SD card write";output.Complete();
    }
    private static void ConfigureTls()
    {
        Status="Configuring HTTPS";
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
