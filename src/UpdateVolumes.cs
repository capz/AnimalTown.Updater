using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Dolphin.Storage;
using Dolphin.Timing;

namespace AnimalTown.Updater;

internal sealed class UpdateVolumes : IDisposable
{
    private readonly List<UpdateVolume> mounted = new List<UpdateVolume>();

    internal async Task<UpdateVolume[]> FindWritableAsync(CancellationToken token, Action<string> reportPath)
    {
        var writable = new List<UpdateVolume>();
        foreach (var volume in UpdateVolume.Supported)
        {
            reportPath(volume.Root);
            await Task.Yield();
            token.ThrowIfCancellationRequested();
            try
            {
                if (!Storage.Mount(volume.Device)) continue;
                mounted.Add(volume);
                string games = volume.Root + "games";
                if (File.Exists(games)) continue;
                // The contiguous writer stages at the volume root before publishing to /games.
                ProbeWrite(volume.Root);
                if (Directory.Exists(games)) ProbeWrite(volume.GamesDirectory);
                writable.Add(volume);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        return writable.ToArray();
    }

    private static void ProbeWrite(string directory)
    {
        string path = directory + ".__dnd_updater_probe_" + Clock.Timestamp;
        bool created = false;
        try
        {
            using var probe = File.Open(path, FileMode.CreateNew, FileAccess.Write);
            created = true;
            var sample = new byte[1];
            probe.Write(sample, 0, sample.Length);
            probe.Flush();
        }
        finally
        {
            if (created) File.Delete(path);
        }
    }

    internal async Task<SwissImage> FindSwissAsync(UpdateVolume preferred, CancellationToken token, Action<string> reportPath)
    {
        var swiss = await SwissDiscovery.TryFindAsync(preferred.Root, token, reportPath);
        if (swiss != null) return swiss;
        foreach (var volume in mounted)
        {
            if (volume == preferred) continue;
            swiss = await SwissDiscovery.TryFindAsync(volume.Root, token, reportPath);
            if (swiss != null) return swiss;
        }
        throw new IOException("No supported Swiss DOL found. Copy an official Swiss normal DOL to a mounted SD card's root.");
    }

    public void Dispose()
    {
        Exception? failure = null;
        foreach (var volume in mounted)
        {
            try
            {
                Storage.Unmount(volume.Device);
                if (Storage.IsMounted(volume.Device)) throw new IOException("Could not release " + volume.Label);
            }
            catch (Exception error) { failure = failure ?? error; }
        }
        if (failure != null) throw failure;
    }
}
