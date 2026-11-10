using System;
using System.Threading;
using Dolphin.Diagnostics;
using Dolphin.Graphics;
using Dolphin.Input;
using Dolphin.IO;

namespace AnimalTown.Updater;
internal static class Program
{
    private static int Main()
    {
        DebugOverlay.PerformanceVisible=false;DebugOverlay.TextVisible=true;
        DebugOverlay.WriteLine("AnimalTown updater");
        using var cancel=new CancellationTokenSource();
        var task=Updater.Run(cancel.Token);string previous="";
        while(!task.IsCompleted){
            Controller.Update();if(Controller.GetState(0).B.WasPressed)cancel.Cancel();
            if(previous!=Updater.Status){previous=Updater.Status;DebugOverlay.WriteLine(previous);}
            Frame();
        }
        try{
            using var boot=task.GetAwaiter().GetResult();DebugOverlay.WriteLine(Updater.Status);
            Dolphin.Storage.Storage.Unmount(Dolphin.Storage.StorageDevice.GcLoader);
            DolLauncher.RequestLaunch(boot);return 0;
        }catch(Exception error){
            DebugOverlay.WriteLine("Update stopped: "+(error.Message??"console I/O failure"));
            DebugOverlay.WriteLine("B: exit. Your original ISO or .iso.bak is retained.");
            while(true){Controller.Update();if(Controller.GetState(0).B.WasPressed)return 1;Frame();}
        }
    }
    private static void Frame()
    {
        var device=GraphicsDevice.Default;device.BeginFrame(new Color32(49,72,62));device.EndFrame();
    }
}
