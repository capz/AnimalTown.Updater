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
            task.GetAwaiter().GetResult();
            Dolphin.Storage.Storage.Unmount(Dolphin.Storage.StorageDevice.GcLoader);
            string finished=Updater.TotalBytes>0?"Update complete. Power off and on to start AnimalTown.":"Already up to date. Power off and on to return to AnimalTown.";
            while(true){Controller.Update();if(Controller.GetState(0).B.WasPressed)return 0;console.Draw(finished,false,false,6,Updater.DownloadedBytes,Updater.TotalBytes,Updater.Artifact);}
        }catch(Exception error){
            string message=error.Message??"";
            if(message.Length==0)message="The console could not complete this operation.";
            string kind=error is OutOfMemoryException?"OutOfMemoryException":error is System.IO.FileNotFoundException?"FileNotFoundException":
                error is System.IO.DirectoryNotFoundException?"DirectoryNotFoundException":error is UnauthorizedAccessException?"UnauthorizedAccessException":
                error is System.IO.IOException?"IOException":error is FormatException?"FormatException":error is ArgumentException?"ArgumentException":
                error is OperationCanceledException?"OperationCanceledException":error is InvalidOperationException?"InvalidOperationException":"Exception";
            string detail=kind+(string.IsNullOrEmpty(error.Message)?" (no native message)":"");
            if(Updater.NativeResult.Length>0)detail+="\n"+Updater.NativeResult;
            if(Updater.CurrentPath.Length>0)detail+="\n"+Updater.CurrentPath;
            while(true){Controller.Update();if(Controller.GetState(0).B.WasPressed)return 1;console.Draw(message,false,true,Updater.Phase,Updater.DownloadedBytes,Updater.TotalBytes,Updater.Artifact,detail,Updater.Operation);}
        }
    }
}
