using System;
using System.Threading;
using AnimalTown.Updater;
using Dolphin.Storage;
using Scheduler=Dolphin.Threading.Tasks.Scheduler;

internal static class Program
{
    private static int Main()
    {
#if SCAN_UI
        Scheduler.ProductiveWaits=true;
        const int imageBytes=10000000;
        var rate=new DownloadSpeed();rate.Update(true,0,imageBytes,0);rate.Update(true,50000,imageBytes,500000);
        if(rate.Text!="800.0 kb/s")throw new Exception("Native kilobit speed formatting failed");
        rate.Update(true,150000,imageBytes,1000000);
        if(rate.Text!="1.6 Mb/s")throw new Exception("Native megabit speed formatting failed");
        if(rate.Eta!="About 1m 19s left to download")throw new Exception("Native download ETA failed");
        rate.Update(false,150000,imageBytes,1000001);
        if(rate.Text!=""||rate.Eta!="")throw new Exception("Native transfer visibility failed");
        using var console=new UpdaterConsole();
        console.Draw("Searching GC Loader for stable AnimalTown images",true,false,0,0,0,"");
#endif
#if SCAN_UPDATER
        Dolphin.IO.Defrag.OptIn(false);Dolphin.IO.Defrag.MinimumFileSize=131072;
        VerifyAllocationScopes();
        // The portable storage backend exposes a fixture directory for dvd:/;
        // physical FST mounting itself requires the console/emulator backend.
        if(!Storage.Mount(StorageDevice.DvdDrive))return 16;
        var scan=UpdateWorker.Run(CancellationToken.None,System.Threading.Tasks.Task.FromResult(false));
#else
        if(!Storage.Mount(StorageDevice.GcLoader))return 10;
        var scan=StableImageDiscovery.ScanAsync("gcloader:/",CancellationToken.None);
#endif
        int pumps=0;
        while(!scan.IsCompleted){
            if(++pumps>100000)throw new Exception("Scan did not settle");
#if SCAN_UI
            Dolphin.Input.Controller.Update();
            console.Draw("Searching GC Loader for stable AnimalTown images",true,false,0,0,0,"");
#endif
#if !SCAN_AUTOMATIC
            Scheduler.Pump(1);
#endif
        }
#if SCAN_UPDATER
        try{scan.GetAwaiter().GetResult();return 12;}
        catch(OutOfMemoryException error){
            if(error.Message!="Directory enumeration failed."||Updater.Phase!=0||Updater.Operation!="Prepare Swiss launch helper"||Updater.DownloadedBytes!=0||Updater.TotalBytes!=0||Updater.LaunchImage!="")throw;
            VerifyAllocationRestored();Storage.Unmount(StorageDevice.GcLoader);
            Console.WriteLine("Native enumeration ENOMEM retained before transfer");
            return 0;
        }
        catch(System.IO.IOException){
            if(Updater.Operation!="Prepare Swiss launch helper"||Updater.Status!="Finding Swiss on the GC Loader drive root")throw;
        }
        VerifyAllocationRestored();
        // Test-only catalog and payload exercise a DOL larger than the entire
        // managed heap. Production root discovery cannot recognize this file.
        var swiss=SwissDiscovery.FindAsync("gcloader:/swiss-streaming-fixture",CancellationToken.None,null,
            new[]{new SwissBuild(123,2097408,"442ed67973d6f2e9a689f7aa823b100915d6da9777b0a2476d55f4e23d6b74d2")});
        while(!swiss.IsCompleted){
            if(++pumps>100000)throw new Exception("Swiss discovery did not settle");
#if SCAN_UI
            Dolphin.Input.Controller.Update();
            console.Draw("Finding Swiss on the GC Loader drive root",true,false,0,0,0,"");
#endif
#if !SCAN_AUTOMATIC
            Scheduler.Pump(1);
#endif
        }
        var helper=swiss.GetAwaiter().GetResult();
        if(helper.Path!="gcloader:/swiss-streaming-fixture/renamed.DOL"||helper.Revision!=123)return 13;
        Console.WriteLine("Native Swiss discovery streamed a 2 MiB executable with the 1 MiB heap");
#else
        var image=scan.GetAwaiter().GetResult();
        if(image.Version!="12.34.56"||image.Path!="gcloader:/boot.GCM")return 11;
#endif
        Storage.Unmount(StorageDevice.GcLoader);
#if SCAN_UPDATER
        Console.WriteLine("Native updater read the launched version without installed-image discovery");
#else
        Console.WriteLine("Native scan selected the expected highest stable image");
#endif
        return 0;
    }
#if SCAN_UPDATER
    private static void VerifyAllocationRestored()
    {
        if(Dolphin.IO.Defrag.IsEnabled||Dolphin.IO.Defrag.MinimumFileSize!=131072)
            throw new Exception("Updater allocation policy leaked past its scope");
    }
    private static void VerifyAllocationScopes()
    {
        using(Dolphin.IO.Defrag.BeginScope(true,65536)){
            if(!Dolphin.IO.Defrag.IsEnabled||Dolphin.IO.Defrag.MinimumFileSize!=65536)throw new Exception("Allocation scope was not enabled");
        }
        VerifyAllocationRestored();
        try{using(Dolphin.IO.Defrag.BeginScope(true,65536)){throw new System.IO.IOException("Test write failed");}}
        catch(System.IO.IOException){}
        VerifyAllocationRestored();
        Console.WriteLine("Native automatic defrag scope: opt-in, threshold and success/failure restoration passed");
    }
#endif
}

#if SCAN_UPDATER
namespace AnimalTown.Updater
{
    internal static class NativeLaunchArguments
    {
        internal static string[] Read()=>new[]{"dvd:/animal-town-updater.dol","--animaltown-update=3","0.22.0"};
    }
}
#endif
