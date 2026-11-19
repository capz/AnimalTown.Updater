using System;
namespace AnimalTown.Updater;

// Recent payload throughput and remaining download time, sampled on the UI executor.
// Clock.Timestamp is monotonic microseconds; ISO byte counts are bounded to Int32.
internal sealed class DownloadSpeed
{
    private const long SampleInterval=500000,StallInterval=1500000;
    private bool active;
    private long bytes,at,lastProgressAt,estimateBitsPerSecond,etaSeconds;
    internal string Text {get;private set;}="";
    internal string Eta {get;private set;}="";
    internal void Update(bool downloading,long received,long total,long now)
    {
        if(!downloading){active=false;Text="";SetEta(0);return;}
        received=Math.Max(0,received);
        if(!active||received<bytes||now<at){
            active=true;
            bytes=received;
            at=lastProgressAt=now;
            estimateBitsPerSecond=0;
            Text="0.0 kb/s";
            SetEta(0);
            return;
        }
        if(total<=received)SetEta(0);
        long elapsed=now-at;
        if(elapsed<SampleInterval)return;
        long transferredBits=(received-bytes)*8;
        long bitsPerSecond=transferredBits*1000000/elapsed;
        Text=ConsoleText.Speed(bitsPerSecond);
        bytes=received;at=now;
        if(bitsPerSecond>0){
            if(estimateBitsPerSecond==0)estimateBitsPerSecond=bitsPerSecond;
            else{
                // Include gaps between writes so bursts don't overstate throughput.
                long recentRate=transferredBits*1000000/(now-lastProgressAt);
                estimateBitsPerSecond=(estimateBitsPerSecond*3+recentRate)/4;
            }
            lastProgressAt=now;
        }else if(now-lastProgressAt>=StallInterval){
            estimateBitsPerSecond=0;
        }
        long seconds=total>received&&estimateBitsPerSecond>0
            ?((total-received)*8+estimateBitsPerSecond-1)/estimateBitsPerSecond
            :0;
        SetEta(seconds);
    }
    private void SetEta(long seconds)
    {
        if(seconds>=3600)seconds=seconds/60*60;
        if(seconds==etaSeconds)return;
        etaSeconds=seconds;
        Eta=seconds>0?ConsoleText.DownloadEta(seconds):"";
    }
}
