using System;
using System.Threading;
using Dolphin.Diagnostics;
using Dolphin.Input;
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
            using var launch=task.GetAwaiter().GetResult();
            Dolphin.Storage.Storage.Unmount(Dolphin.Storage.StorageDevice.GcLoader);
            if(Dolphin.Storage.Storage.IsMounted(Dolphin.Storage.StorageDevice.GcLoader))throw new System.IO.IOException("Could not release GC Loader before Swiss handoff");
            Dolphin.IO.DolLauncher.RequestLaunch(launch);
            return 0;
        }catch(Exception error){
            string message=error.Message??"";
            if(message.Length==0)message="The console could not complete this operation.";
            string kind=error is OutOfMemoryException?"OutOfMemoryException":error is System.IO.FileNotFoundException?"FileNotFoundException":
                error is System.IO.DirectoryNotFoundException?"DirectoryNotFoundException":error is UnauthorizedAccessException?"UnauthorizedAccessException":
                error is System.IO.IOException?"IOException":error is FormatException?"FormatException":error is ArgumentException?"ArgumentException":
                error is OperationCanceledException?"OperationCanceledException":error is TimeoutException?"TimeoutException":
                error is InvalidOperationException?"InvalidOperationException":"Exception";
            string detail=kind+(string.IsNullOrEmpty(error.Message)?" (no native message)":"");
            if(Updater.NativeResult.Length>0)detail+="\n"+Updater.NativeResult;
            if(Updater.CurrentPath.Length>0)detail+="\n"+Updater.CurrentPath;
            while(true){Controller.Update();if(Controller.GetState(0).B.WasPressed)return 1;console.Draw(message,false,true,Updater.Phase,Updater.DownloadedBytes,Updater.TotalBytes,Updater.Artifact,detail,Updater.Operation);}
        }
    }
}
