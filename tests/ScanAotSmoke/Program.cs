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
            if(Updater.Operation!="Prepare Swiss launch helper"||Updater.Status!="Selected 12.34.56 in gcloader:/")throw;
        }
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
