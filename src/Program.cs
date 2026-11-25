using System;
using System.Threading;
using System.Threading.Tasks;
using Dolphin.Diagnostics;
using Dolphin.Input;
namespace AnimalTown.Updater;
internal static class Program
{
    private static int Main()
    {
        DebugOverlay.PerformanceVisible=false;DebugOverlay.TextVisible=false;
        Dolphin.Threading.Tasks.Scheduler.ProductiveWaits=true;
        using var console=new UpdaterConsole();
        using var cancel=new CancellationTokenSource();
        var cleanupChoice=new TaskCompletionSource<bool>();
        bool cleanupEnabled=false;
        // Present the console before storage/network initialization can fail.
        console.Draw(Updater.Status,true,false,0,0,0,"");
        var task=UpdateWorker.Run(cancel.Token,cleanupChoice.Task);
        while(!task.IsCompleted){
            Controller.Update();
            var input=Controller.GetState(0);
            int phase=Updater.Phase;
            if(!cleanupChoice.Task.IsCompleted){
                if(input.B.WasPressed)cancel.Cancel();
                if(input.X.WasPressed)cleanupEnabled=!cleanupEnabled;
                // The worker waits here for the last choice shown by the UI.
                if(phase==Updater.InstallationPhase)cleanupChoice.SetResult(cleanupEnabled);
            }
            console.Draw(Updater.Status,true,false,phase,Updater.DownloadedBytes,Updater.TotalBytes,Updater.Artifact,
                summary:Updater.ReleaseSummary,updateVersion:Updater.ReleaseVersion,cleanupEnabled:cleanupEnabled);
        }
        try{
            using var launch=task.GetAwaiter().GetResult();
            Dolphin.Storage.Storage.Unmount(Dolphin.Storage.StorageDevice.GcLoader);
            if(Dolphin.Storage.Storage.IsMounted(Dolphin.Storage.StorageDevice.GcLoader))throw new System.IO.IOException("Could not release GC Loader after the update check");
            if(launch==null){
                while(true){
                    Controller.Update();if(Controller.GetState(0).B.WasPressed)return 0;
                    console.Draw(Updater.Status,false,false,Updater.Phase,0,0,"",IsoVersion.DiscPath,Updater.Operation);
                }
            }
            if(Updater.CleanupWarning.Length>0){
                while(true){
                    Controller.Update();
                    if(Controller.GetState(0).A.WasPressed)break;
                    console.Draw("AnimalTown is ready to start",false,true,Updater.InstallationPhase,
                        Updater.DownloadedBytes,Updater.TotalBytes,Updater.Artifact,Updater.CleanupWarning,cleanupWarning:true);
                }
            }
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
