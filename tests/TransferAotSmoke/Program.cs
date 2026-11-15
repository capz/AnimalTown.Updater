using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AnimalTown.Updater;
class Program {
 static int written,writes,progress,mode;
 static void Write(DownloadBlock block){byte[] b=block.Buffer;int o=0,n=block.Count;if(mode==2)throw new IOException("injected write");if(n!=16384&&written+n!=65567)throw new Exception("Write shape");for(int i=0;i<n;i++)if(b[o+i]!=(byte)((written+i)%251))throw new Exception("Byte order");written+=n;writes++;}
 static void Progress(int n){if(n!=written)throw new Exception("Progress");progress=n;}
 static int Main(){try{
 foreach(int chunk in new[]{4096,5003,16384}){
 written=writes=progress=mode=0;using var stream=new GeneratedStream(65567,chunk,null,0);
 DownloadTransfer.CopyAsync(stream,Write,65567,"87a6b36b4acef90683b20054c3afe6396b37f27c3cf5ce3f4f10ee9686537ce4",Progress,default).GetAwaiter().GetResult();
 if(written!=65567||progress!=65567||writes!=5||stream.SmallRequests!=0)return 1;
 Console.WriteLine("BATCH chunk="+chunk+" reads="+stream.Reads+" writes="+writes+" bytes="+written);
 }
 for(int error=1;error<=6;error++){
 mode=error==2?2:0;written=writes=progress=0;
 using var stop=new CancellationTokenSource();using var stream=new GeneratedStream(65567,4096,error==3?stop:null,error==4?4096:0);
 bool rejected=false;
 try{DownloadTransfer.CopyAsync(stream,Write,error==5?65566:error==6?65568:65567,error==1?"bad":"87a6b36b4acef90683b20054c3afe6396b37f27c3cf5ce3f4f10ee9686537ce4",Progress,stop.Token).GetAwaiter().GetResult();}
 catch(OperationCanceledException){rejected=error==3;}
 catch(IOException){rejected=error!=3;}
 if(!rejected)return 2;if((error==2||error==3)&&progress!=0)return 3;
 }
 return 0;
 }catch(Exception error){Console.WriteLine(error.Message);return 99;}}
}
sealed class GeneratedStream:Dolphin.IO.Stream {
 readonly int size,chunk,failAt;readonly CancellationTokenSource? cancel;int position;
 public int Reads,SmallRequests;
 internal GeneratedStream(int n,int limit,CancellationTokenSource? stop,int fail){size=n;chunk=limit;cancel=stop;failAt=fail;}
 public override bool CanRead=>true;public override bool CanWrite=>false;public override bool CanSeek=>false;
 public override long Length=>size;public override long Position{get=>position;set=>throw new NotSupportedException();}
 public override Task<int> ReadAsync(byte[] b,int o,int n,CancellationToken token){token.ThrowIfCancellationRequested();if(n!=16384)SmallRequests++;if(failAt>0&&position>=failAt)throw new IOException("injected read");int count=Math.Min(chunk,size-position);for(int i=0;i<count;i++)b[o+i]=(byte)((position+i)%251);position+=count;Reads++;if(cancel!=null)cancel.Cancel();return Task.FromResult(count);}
 public override int Read(byte[] b,int o,int n)=>throw new NotSupportedException();
 public override void Write(byte[] b,int o,int n)=>throw new NotSupportedException();
 public override long Seek(long n,SeekOrigin o)=>throw new NotSupportedException();
 public override void SetLength(long n)=>throw new NotSupportedException();public override void Flush(){}
}
