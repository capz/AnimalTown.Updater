using Dolphin.Storage;

namespace AnimalTown.Updater;

internal sealed class UpdateVolume
{
    internal static readonly UpdateVolume[] Supported = {
        new UpdateVolume(StorageDevice.GcLoader, "GC Loader SD", "gcloader:/"),
        new UpdateVolume(StorageDevice.SdSerialPort2, "SD2SP2", "sd:/"),
        new UpdateVolume(StorageDevice.SdSlotA, "SD Gecko (Slot A)", "carda:/"),
        new UpdateVolume(StorageDevice.SdSlotB, "SD Gecko (Slot B)", "cardb:/")
    };

    internal readonly StorageDevice Device;
    internal readonly string Label, Root, GamesDirectory;

    private UpdateVolume(StorageDevice device, string label, string root)
    {
        Device = device;
        Label = label;
        Root = root;
        GamesDirectory = root + "games/";
    }
}
