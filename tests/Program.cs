using System.Security.Cryptography;
using System.Text;
using AnimalTown.Updater;

int checks=0;
void Check(bool value,string name){checks++;if(!value)throw new Exception(name);}
void Reject(Action action,string name){bool rejected=false;try{action();}catch(Exception){rejected=true;}Check(rejected,name);}
foreach(int size in new[]{0,1,3,55,56,63,64,65,127,128,1000,1000000}){
    var data=new byte[size];for(int i=0;i<size;i++)data[i]=(byte)(i%251);var sha=new Sha256();
    for(int at=0;at<size;at+=17)sha.Append(data,at,Math.Min(17,size-at));
    Check(sha.Finish()==Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant(),"SHA-256 boundary / multiblock "+size);
}
Check(Enumerable.Range(0,7).Select(ConsoleText.Dots).SequenceEqual(new[]{0,1,3,7,6,4,0}),"dots appear and disappear left to right");
Check(ConsoleText.Transfer(1572864,10485760)=="1.5 MiB / 10.0 MiB","binary MiB progress units");
Check(ConsoleText.Percent(999,1000)==99&&ConsoleText.Percent(1200,1000)==100&&ConsoleText.Percent(-1,1000)==0,"bounded exact download percentage");
var wrapped=new string[3];int rows=ConsoleText.Wrap("A verylongfilename.iso and another line",10,s=>s.Length,wrapped);
Check(rows==3&&wrapped.All(s=>s.Length<=10)&&wrapped[2].EndsWith("..."),"safe-area wrapping bounds long filenames and truncated errors");
string json="""
[{"tag_name":"v0.14.9.abcdef123456","draft":false,"prerelease":true,"ignored":{"nested":[1,true,null]},"assets":[{"name":"AnimalTown-0.14.9.abcdef123456.iso","size":1459978240},{"name":"AnimalTown-0.14.9.abcdef123456.nkit.iso","size":93001728,"browser_download_url":"https://github.com/capz/AnimalTown/releases/download/v0.14.9.abcdef123456/AnimalTown-0.14.9.abcdef123456.nkit.iso","digest":"sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef"}]}]
""";
var release=ReleaseInfo.Parse(json);Check(release.Size==93001728&&release.Version=="0.14.9.abcdef123456","select direct NKit development release");
Check(ReleaseInfo.NeedsUpdate("0.13.9.111111111111",release.Version),"new feature counter");
Check(!ReleaseInfo.NeedsUpdate(release.Version,release.Version),"same release skipped");
Check(ReleaseInfo.NeedsUpdate("0.15.9.111111111111",release.Version),"newest publication wins when squash merging lowers first-parent counters");
Check(ReleaseInfo.NeedsUpdate("0.15.8.111111111111",release.Version),"independent feature/fix counters follow newest publication when incomparable");
Check(ReleaseInfo.NeedsUpdate("0.14.9.111111111111",release.Version),"opaque commit identity uses newest publication");
Reject(()=>ReleaseInfo.Parse(json.Replace("\"draft\":false","\"draft\":true")),"draft rejected");
Reject(()=>ReleaseInfo.Parse(json.Replace("sha256:","sha512:")),"missing SHA-256 rejected");
Reject(()=>ReleaseInfo.Parse(json.Replace("https://github.com/capz/AnimalTown/","http://example.com/")),"foreign / plaintext asset rejected");
Reject(()=>ReleaseInfo.Parse("[]"),"no releases");
foreach(string version in new[]{"0.1.2","v0.1.2.abcdef123456","0.1.2.../payload","0.1.2.abcdef123456-dirty","0.99999999999.0.abcdef123456"})Reject(()=>ReleaseInfo.ParseVersion(version),"invalid version");
const string image="AnimalTown.iso",download="AnimalTown-0.14.9.abcdef123456.nkit.iso";
for(int failure=1;failure<=6;failure++){
    var files=new FakeFiles{FailAt=failure};files.Data[image]="original";files.Data[image+".bak"]="older";files.Data[download]="new";
    try{InstallTransaction.Commit(files,image,download);}catch(IOException){}
    files.FailAt=0;InstallTransaction.Recover(files,image);InstallTransaction.Recover(files,image);
    Check(files.Data.ContainsKey(image),"canonical restored at fault "+failure);
    Check(files.Data[image]=="original"||files.Data[image]=="new"&&files.Data[image+".bak"]=="original","complete original/new retained at fault "+failure);
}
var rollbackFailure=new FakeFiles{FailAt=4,FailAgain=5};rollbackFailure.Data[image]="original";rollbackFailure.Data[image+".bak"]="older";rollbackFailure.Data[download]="new";
try{InstallTransaction.Commit(rollbackFailure,image,download);}catch(IOException){}
rollbackFailure.FailAt=rollbackFailure.FailAgain=0;InstallTransaction.Recover(rollbackFailure,image);Check(rollbackFailure.Data[image]=="original","reboot recovery survives a failed immediate rollback");
var success=new FakeFiles();success.Data[image]="original";success.Data[download]="new";InstallTransaction.Commit(success,image,download);
Check(success.Data[image]=="new"&&success.Data[image+".bak"]=="original"&&!success.Exists(download),"requested rename / backup final state");
var cut=new FakeFiles();cut.Data[image+".bak"]="original";cut.Mark(image+".update");cut.Data[download]="new";InstallTransaction.Recover(cut,image);
Check(cut.Data[image]=="original"&&cut.Data[download]=="new","power cut between renames restores original and retains download");
var unknown=new FakeFiles();unknown.Data[image+".update"]="unknown";Reject(()=>InstallTransaction.Recover(unknown,image),"unknown marker preserved");
string temporary=Path.Combine(Path.GetTempPath(),"animaltown-updater-"+Guid.NewGuid());Directory.CreateDirectory(temporary);
try{
    string path=Path.Combine(temporary,image);var header=new byte[0x440];Encoding.ASCII.GetBytes("AMTE01").CopyTo(header,0);new byte[]{0xc2,0x33,0x9f,0x3d}.CopyTo(header,0x1c);Encoding.ASCII.GetBytes("AnimalTown / "+release.Version).CopyTo(header,0x20);File.WriteAllBytes(path,header);
    Check(ImageFiles.ReadVersion(path)==release.Version,"standalone updater reads installed version from ISO header");
    header[0]=0;File.WriteAllBytes(path,header);Reject(()=>ImageFiles.ReadVersion(path),"foreign image preserved");
}finally{Directory.Delete(temporary,true);}
if(args.Length==1){var live=ReleaseInfo.Parse(File.ReadAllText(args[0]));Check(live.Name.EndsWith(".nkit.iso"),"actual GitHub release metadata");Console.WriteLine("Live release parsed: "+live.Name);}
Console.WriteLine(checks+" updater checks passed: SHA-256, release selection, version comparison, image identity and interrupted installation.");

sealed class FakeFiles : IInstallFiles
{
    internal readonly Dictionary<string,string> Data=new();internal int FailAt,FailAgain;private int mutations;
    private void Mutate(){if(++mutations==FailAt||mutations==FailAgain)throw new IOException("Injected I/O failure");}
    public bool Exists(string p)=>Data.ContainsKey(p);
    public void Move(string a,string b){Mutate();if(!Data.ContainsKey(a)||Data.ContainsKey(b))throw new IOException();Data[b]=Data[a];Data.Remove(a);}
    public void Delete(string p){Mutate();Data.Remove(p);}
    public void Mark(string p){Mutate();Data.Add(p,"ATUPDATE1");}
    public bool HasMarker(string p)=>Data[p]=="ATUPDATE1";
}
