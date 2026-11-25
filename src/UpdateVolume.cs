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
    internal readonly bool CanBoot;

    private UpdateVolume(StorageDevice device, string label, string root, bool canBoot = true)
    {
        Device = device;
        Label = label;
        Root = root;
        GamesDirectory = root + "games/";
        CanBoot = canBoot;
    }

    internal UpdateVolume WithMountedMedia(StorageMedia media)
    {
        if (media == StorageMedia.SdSerialPort1) return new UpdateVolume(Device, "SD2SP1", Root);
        if (media != StorageMedia.Mmce) return this;
        string port = Device == StorageDevice.SdSlotA ? "Slot A" :
            Device == StorageDevice.SdSlotB ? "Slot B" : "Serial Port 2";
        return new UpdateVolume(Device, "MMCE (" + port + ", save only)", Root, false);
    }
}
