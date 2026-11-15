using System;
using System.Threading;
using System.Threading.Tasks;
using Dolphin.Graphics;
using Dolphin.IO;
using Scheduler=Dolphin.Threading.Tasks.Scheduler;
namespace AnimalTown.Updater;

internal static class Program
{
    private static int Main()
    {
        Scheduler.ProductiveWaits=true;
        var device=GraphicsDevice.Default;
        var task=UpdateWorker.Run(CancellationToken.None);
        int frames=0;
        while(!task.IsCompleted){
            device.BeginFrame(new Color32(0,0,0));device.EndFrame();
            if(++frames>500)throw new Exception("Worker did not finish");
        }
        task.GetAwaiter().GetResult();
        if(Updater.Steps!=128)throw new Exception("Incomplete worker");
        using var cancel=new CancellationTokenSource();cancel.Cancel();
        var cancelled=UpdateWorker.Run(cancel.Token);
        while(!cancelled.IsCompleted){device.BeginFrame(new Color32(0,0,0));device.EndFrame();}
        try{cancelled.GetAwaiter().GetResult();throw new Exception("Cancellation lost");}catch(OperationCanceledException){}
        Updater.Fail=true;
        var failed=UpdateWorker.Run(CancellationToken.None);
        while(!failed.IsCompleted){device.BeginFrame(new Color32(0,0,0));device.EndFrame();}
        try{failed.GetAwaiter().GetResult();throw new Exception("Failure lost");}catch(System.IO.IOException){}
        Console.WriteLine("Worker background context, continuations, cancellation and failure passed");
        return 0;
    }
}

// Scheduling workload only: this models bounded read completions, not BBA I/O.
internal static class Updater
{
    internal static int Steps;
    internal static bool Fail;
    internal static async Task<PreparedDol> Run(CancellationToken token)
    {
        if(Scheduler.IsApplicationThread)throw new Exception("Updater started on UI executor");
        if(Fail)throw new System.IO.IOException("Fixture failure");
        for(int i=0;i<128;i++){
            token.ThrowIfCancellationRequested();await Task.Yield();
            if(Scheduler.IsApplicationThread)throw new Exception("Continuation moved to UI executor");
            Steps++;
        }
        return null!;
    }
}
