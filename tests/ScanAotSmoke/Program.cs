using System;
using System.Threading;
using System.Threading.Tasks;
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
        bool multi=System.IO.File.Exists("dvd:/multi-destination");
        bool sdOnly=System.IO.File.Exists("dvd:/sd-only");
        var destinationChoice=new TaskCompletionSource<int>();
        var scan=UpdateWorker.Run(CancellationToken.None,destinationChoice.Task,Task.FromResult(false));
#else
        if(!Storage.Mount(StorageDevice.GcLoader))return 10;
        var scan=StableImageDiscovery.ScanAsync("gcloader:/",CancellationToken.None);
#endif
        int pumps=0;
        while(!scan.IsCompleted){
            if(++pumps>100000)throw new Exception("Scan did not settle");
#if SCAN_UPDATER
            var destinations=Updater.Destinations;
            if(destinations.Length>0&&!destinationChoice.Task.IsCompleted){
                if(!multi||destinations.Length!=3||destinations[0].Root!="gcloader:/"||destinations[1].Root!="sd:/"||destinations[2].Root!="cardb:/")
                    throw new Exception("Writable destination list is incorrect");
#if SCAN_UI
                console.DrawDestinations(destinations,1);
#endif
                if(Updater.Operation!="Choose update destination"||Updater.Phase!=0)throw new Exception("Update began before destination was chosen");
                destinationChoice.SetResult(1);
            }
#endif
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
            VerifyAllocationRestored();
            Console.WriteLine("Native enumeration ENOMEM retained before transfer");
            return 0;
        }
        catch(System.IO.IOException){
            if(Updater.Operation!="Prepare Swiss launch helper"||Updater.Status!="Finding Swiss on mounted SD cards")throw;
        }
        VerifyAllocationRestored();
        if(Storage.IsMounted(StorageDevice.GcLoader)||Storage.IsMounted(StorageDevice.SdSerialPort2)||Storage.IsMounted(StorageDevice.SdSlotA)||Storage.IsMounted(StorageDevice.SdSlotB))
            throw new Exception("An SD volume remained mounted after the update stopped");
        if(Updater.DestinationLabel!=(multi||sdOnly?"SD2SP2":"GC Loader SD")||destinationChoice.Task.IsCompleted!=multi)
            throw new Exception("Updater did not use the expected destination");
        var helperDevice=sdOnly?StorageDevice.SdSerialPort2:StorageDevice.GcLoader;
        string helperRoot=sdOnly?"sd:/":"gcloader:/";
        if(!Storage.Mount(helperDevice))return 17;
        // Test-only catalog and payload exercise a DOL larger than the entire
        // managed heap. Production root discovery cannot recognize this file.
        var swiss=SwissDiscovery.FindAsync(helperRoot+"swiss-streaming-fixture",CancellationToken.None,null,
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
        if(helper.Path!=helperRoot+"swiss-streaming-fixture/renamed.DOL"||helper.Revision!=123)return 13;
        Storage.Unmount(helperDevice);
        Console.WriteLine("Native Swiss discovery streamed a 2 MiB executable with the 1 MiB heap");
        if(multi||sdOnly)Console.WriteLine("Native updater used SD2SP2 with the expected destination prompt behavior");
#else
        var image=scan.GetAwaiter().GetResult();
        if(image.Version!="12.34.56"||image.Path!="gcloader:/boot.GCM")return 11;
        Storage.Unmount(StorageDevice.GcLoader);
#endif
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
