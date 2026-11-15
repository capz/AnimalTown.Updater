using System;
namespace AnimalTown.Updater;

// Recent payload throughput, sampled on the UI executor without touching I/O.
// Clock.Timestamp is monotonic microseconds; ISO byte counts are bounded to Int32.
internal sealed class DownloadSpeed
{
    private bool active;
    private long bytes,at;
    internal string Text {get;private set;}="";
    internal void Update(bool downloading,long received,long now)
    {
        if(!downloading){active=false;Text="";return;}
        received=Math.Max(0,received);
        if(!active||received<bytes||now<at){
            active=true;bytes=received;at=now;Text="0.0 kb/s";return;
        }
        long elapsed=now-at;
        if(elapsed<500000)return;
        Text=ConsoleText.Speed((received-bytes)*8000000/elapsed);
        bytes=received;at=now;
    }
}
