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
            // Five seconds per deterministic state. Failure details are sample
            // diagnostics, not results from actual SD/network operations.
            int scene=(int)((Clock.Timestamp-start)/5000000%8);
            switch(scene){
                case 0:console.Draw("Connecting Broadband Adapter",true,false,1,0,0,"");break;
                case 1:console.Draw("Downloading release image",true,false,3,44040192,93001728,"AnimalTown-0.12.13.abcdef123456.nkit.iso");break;
                case 2:console.Draw("No game image path was supplied.",false,true,0,0,0,"","ArgumentException\nStart from AnimalTown or pass an image path.","Resolve installed game image");break;
                case 3:console.Draw("The data card could not be mounted.",false,true,0,0,0,"","GC Loader mount returned false.\ngcloader:/","Mount writable data volume");break;
                case 4:console.Draw("The installed AnimalTown image could not be read from the selected data volume.",false,true,0,0,0,"","IOException / native result: -1\ngcloader:/games/AnimalTown/Preview-builds/resident-onboarding/AnimalTown-0.11.15+abcdef123456/AnimalTown-0.11.15+abcdef123456.nkit.iso","Read installed image header and validate AnimalTown identity before installing an update");break;
                case 5:console.Draw("HTTPS configuration could not be read.",false,true,1,0,0,"","IOException (no native message)\ngcloader:/animaltown-updater/roots.pem","Read HTTPS trust and fresh entropy");break;
                case 6:console.Draw("The network could not be started.",false,true,1,0,0,"","IOException (no native message) / native result: -1\nBroadband Adapter: no link detected.","Start Broadband Adapter");break;
                default:console.Draw("Update complete. Power off and on to start AnimalTown.",false,false,6,93001728,93001728,"AnimalTown-0.11.24+abcdef123456.nkit.iso");break;
            }
        }
    }
}
