using System;
using System.Threading;
using Dolphin.Diagnostics;
using Dolphin.Input;
using Dolphin.IO;
namespace AnimalTown.Updater;
internal static class Program
{
    private static int Main()
    {
        DebugOverlay.PerformanceVisible=false;DebugOverlay.TextVisible=false;
        using var console=new UpdaterConsole();
        using var cancel=new CancellationTokenSource();
        // Present the console before storage/network initialization can fail.
        console.Draw(Updater.Status,true,false,0,0,0,"");
        var task=Updater.Run(cancel.Token);
        while(!task.IsCompleted){
            Controller.Update();if(Controller.GetState(0).B.WasPressed)cancel.Cancel();
            console.Draw(Updater.Status,true,false,Updater.Phase,Updater.DownloadedBytes,Updater.TotalBytes,Updater.Artifact);
        }
        try{
            using var boot=task.GetAwaiter().GetResult();
            console.Draw("Launching AnimalTown",false,false,6,Updater.DownloadedBytes,Updater.TotalBytes,Updater.Artifact);
            Dolphin.Storage.Storage.Unmount(Dolphin.Storage.StorageDevice.GcLoader);
            DolLauncher.RequestLaunch(boot);return 0;
        }catch(Exception error){
            string message=error.Message??"Console I/O failure";if(message.Length==0)message="Console I/O failure";
            while(true){Controller.Update();if(Controller.GetState(0).B.WasPressed)return 1;console.Draw(message,false,true,Updater.Phase,Updater.DownloadedBytes,Updater.TotalBytes,Updater.Artifact);}
        }
    }
}
