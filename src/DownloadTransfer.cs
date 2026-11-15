using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Stream=Dolphin.IO.Stream;
namespace AnimalTown.Updater;

// One bounded workspace keeps 16 KiB reads intact while coalescing short reads.
// Only successful writes advance progress; publication/readback stays with caller.
internal static class DownloadTransfer
{
    internal const int BatchBytes=16384;
    internal static async Task CopyAsync(Stream body,Action<DownloadBlock> write,int expectedBytes,
        string expectedDigest,Action<int> progress,CancellationToken token)
    {
        var block=new DownloadBlock();var buffer=block.Buffer;var hash=new Sha256();int pending=0,received=0,written=0;
        while(true){
            token.ThrowIfCancellationRequested();
            int count=await body.ReadAsync(buffer,pending,BatchBytes,token);
            if(count==0)break;
            if(count<0||count>BatchBytes||count>expectedBytes-received)throw new IOException("Download exceeds release length");
            received+=count;pending+=count;
            if(pending>=BatchBytes){
                token.ThrowIfCancellationRequested();block.Count=BatchBytes;write(block);hash.Append(buffer,0,BatchBytes);
                written+=BatchBytes;progress(written);pending-=BatchBytes;
                if(pending>0)Array.Copy(buffer,BatchBytes,buffer,0,pending);
            }
        }
        token.ThrowIfCancellationRequested();
        if(received!=expectedBytes)throw new IOException("Incomplete or damaged release; original ISO preserved");
        if(pending>0){block.Count=pending;write(block);hash.Append(buffer,0,pending);written+=pending;progress(written);}
        if(hash.Finish()!=expectedDigest)throw new IOException("Incomplete or damaged release; original ISO preserved");
    }
}

// Reused by a synchronous writer callback; consumers must not retain its buffer.
internal sealed class DownloadBlock
{
    internal readonly byte[] Buffer=new byte[DownloadTransfer.BatchBytes*2];
    internal int Count;
}
