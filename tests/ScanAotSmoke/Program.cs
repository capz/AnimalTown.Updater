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
        using var console=new UpdaterConsole();
        console.Draw("Searching GC Loader for stable AnimalTown images",true,false,0,0,0,"");
#endif
#if SCAN_UPDATER
        var scan=Updater.Run(CancellationToken.None);
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
            if(error.Message!="Directory enumeration failed."||Updater.Phase!=0||Updater.Operation!="Find highest stable installation"||Updater.DownloadedBytes!=0||Updater.TotalBytes!=0||Updater.LaunchImage!="")throw;
            Storage.Unmount(StorageDevice.GcLoader);
            Console.WriteLine("Native enumeration ENOMEM retained before transfer");
            return 0;
        }
        catch(System.IO.IOException){
            if(Updater.Operation!="Prepare Swiss launch helper"||Updater.Status!="Finding Swiss on the GC Loader drive root")throw;
        }
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
        if(image.Version!="12.34.56"||image.Path!="gcloader:/AnimalTown-12.34.56.nkit.iso")return 11;
#endif
        Storage.Unmount(StorageDevice.GcLoader);
        Console.WriteLine("Native scan selected the expected highest stable image");
        return 0;
    }
}

#if SCAN_UPDATER
namespace AnimalTown.Updater
{
    internal static class NativeLaunchArguments
    {
        internal static string[] Read()=>new[]{"dvd:/animal-town-updater.dol","--animaltown-update=2","0.11.36+060a89c8295e"};
    }
}
#endif
