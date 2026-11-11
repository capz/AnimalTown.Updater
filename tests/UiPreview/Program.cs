using AnimalTown.Updater;
using Dolphin.Diagnostics;
using Dolphin.Input;
using Dolphin.Timing;

internal static class Program
{
    private static int Main()
    {
        DebugOverlay.TextVisible=false;DebugOverlay.PerformanceVisible=false;
        using var console=new UpdaterConsole();long start=Clock.Timestamp;
        while(true){
            Controller.Update();if(Controller.GetState(0).Start.WasPressed)return 0;
            int scene=(int)((Clock.Timestamp-start)/1000000%20);
            if(scene<5)console.Draw("Connecting Broadband Adapter",true,false,1,0,0,"");
            else if(scene<15)console.Draw("Downloading release image",true,false,3,44040192,93001728,"AnimalTown-0.12.13.abcdef123456.nkit.iso");
            else console.Draw("GC Loader SD is unavailable or not writable",false,true,0,0,0,"");
        }
    }
}
