using Dolphin.IO;
namespace AnimalTown.Updater;

internal static class SwissLaunch
{
    internal static string[] Arguments(string executable,string image)=>new[]{
        executable,"Autoload="+DiscLauncher.GetSwissImagePath(image),
        "AutoBoot=Yes","BS2Boot=No","Prefer Clean Boot=No"
    };
}
