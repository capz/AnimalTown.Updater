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
foreach(string version in new[]{"0.11.24","0.11.24+abcdef123456","0.14.9.abcdef123456","0.14.9.0123456789abcdef0123456789abcdef01234567"}){
    Check(ReleaseInfo.ParseVersion(version).Length==3,"main, review and legacy build identities remain readable");
}
foreach(string version in new[]{"","0.1","0.1.2.","0.1.2+","v0.1.2.abcdef123456","0.1.2.../payload","0.1.2.abcdef123456-dirty","0.99999999999.0.abcdef123456","0.11.24+abcdef12345","0.11.24+abcdef1234567","0.11.24+0123456789abcdef0123456789abcdef01234567","0.11.24+abcdeg123456","0.11.24+abcdef123456\n","0.11.24 "})Reject(()=>ReleaseInfo.ParseVersion(version),"invalid version");
var mainRelease=ReleaseInfo.Parse(json.Replace("0.14.9.abcdef123456","0.11.24"));
var reviewRelease=ReleaseInfo.Parse(json.Replace("0.14.9.abcdef123456","0.11.24+abcdef123456"));
Check(mainRelease.Name=="AnimalTown-0.11.24.nkit.iso"&&reviewRelease.Name=="AnimalTown-0.11.24+abcdef123456.nkit.iso","current numeric and review release assets are selected by their exact identities");
Check(!ReleaseInfo.NeedsUpdate("0.11.24","0.11.24")&&ReleaseInfo.NeedsUpdate("0.14.9.abcdef123456","0.11.24")&&ReleaseInfo.NeedsUpdate("0.11.24+abcdef123456","0.11.24"),"newest published identity remains authoritative across current and legacy formats");
string publishedJson=File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Fixtures","release-review.json"));
var published=ReleaseInfo.Parse(publishedJson);
Check(published.Version=="0.11.24+1cc478a97ab0"&&published.Name=="AnimalTown-0.11.24+1cc478a97ab0.nkit.iso"&&published.Size==92655616,"actual published review metadata with percent-encoded plus signs remains usable");
foreach(string url in new[]{published.Url.Replace("%2B","+"),published.Url.Replace("v0.11.24%2B","v0.11.24+"),published.Url.Replace("/AnimalTown-0.11.24%2B","/AnimalTown-0.11.24+")})
    Check(ReleaseInfo.Parse(publishedJson.Replace(published.Url,url)).Name==published.Name,"literal and percent-encoded review tag/asset URL spellings identify the same exact asset");
foreach(string url in new[]{published.Url.Replace("/AnimalTown-0.11.24%2B1cc478a97ab0.nkit.iso","/other.nkit.iso"),published.Url.Replace("/v0.11.24%2B1cc478a97ab0/","/v0.11.25/"),published.Url+"/other.iso",published.Url+"?download=1",published.Url.Replace("github.com/","github.com@evil.example/"),published.Url.Replace("%2B","%252B")})
    Reject(()=>ReleaseInfo.Parse(publishedJson.Replace(published.Url,url)),"release download URL must identify the exact repository, tag and published basename");
string[] Request(string path,string version="0.11.24")=>new[]{"dvd:/animal-town-updater.dol",LaunchRequest.Protocol,path,version};
foreach(string path in new[]{"gcloader:/My Town.iso","gcloader:/Games/Animal Town/AnimalTown-0.11.24.nkit.iso","gcloader:/Games/Autumn.GCM","gcloader:/街/Forêt 🦊.nkit.iso"}){
    var request=LaunchRequest.Parse(Request(path));
    Check(request.Image==path&&request.Backup==path+".bak"&&request.Marker==path+".update","a launch request preserves the actual root, subdirectory, spaced or Unicode image name");
    Check(request.Directory+path.Substring(path.LastIndexOf('/')+1)==path,"directory paths preserve the exact target");
    Check(request.Root=="gcloader:/"&&request.DownloadPath(mainRelease.Name)==request.Directory+".animaltown-update/"+mainRelease.Name,"updater settings stay at volume root while staging follows the image directory");
    request.VerifyInstalledVersion("0.11.24");Reject(()=>request.VerifyInstalledVersion("0.11.25"),"a changed installed identity is refused before replacement");
}
foreach(string[] arguments in new[]{Array.Empty<string>(),new[]{"updater.dol"},new[]{"updater.dol",LaunchRequest.Protocol,"gcloader:/Town.iso"},new[]{"updater.dol",LaunchRequest.Protocol,"gcloader:/Town.iso","0.11.24","extra"},new[]{"updater.dol","--animaltown-update=2","gcloader:/Town.iso","0.11.24"},new[]{"",LaunchRequest.Protocol,"gcloader:/Town.iso","0.11.24"}})Reject(()=>LaunchRequest.Parse(arguments),"missing, extra or unsupported request arguments never guess a target");
foreach(string path in new[]{"AnimalTown.iso","/AnimalTown.iso","dvd:/AnimalTown.iso","sd:/AnimalTown.iso","carda:/AnimalTown.iso","gcldr:/AnimalTown.iso","gcloader:Town.iso","gcloader:/","gcloader://Town.iso","gcloader:/games/../Town.iso","gcloader:/./Town.iso","gcloader:/games//Town.iso","gcloader:/games/Town.iso/","gcloader:/Town.iso\0","gcloader:/Town\n.iso","gcloader:/Town\\name.iso","gcloader:/Town=AutoBoot.iso","gcloader:/Town?.iso","gcloader:/Town*.iso","gcloader:/Town\".iso","gcloader:/Town<.iso","gcloader:/Town>.iso","gcloader:/Town|.iso","gcloader:/games /Town.iso","gcloader:/games./Town.iso","gcloader:/Town.iso ","gcloader:/Town.iso.","gcloader:/Town.dol","gcloader:/Town.iso.bak","gcloader:/Town.nkit","gcloader:/bad\ud800.iso","gcloader:/bad\udc00.iso","gcloader:/"+new string('a',245)+".iso","gcloader:/"+new string('界',250)+"/"+new string('界',100)+"/Town.iso"})Reject(()=>LaunchRequest.Parse(Request(path)),"noncanonical, wrong-device, unsafe or oversized targets are rejected");
var colliding=LaunchRequest.Parse(Request("gcloader:/Games/AnimalTown-0.11.24.nkit.iso","0.11.23"));
Check(colliding.DownloadPath(mainRelease.Name)!=colliding.Image&&colliding.DownloadPath(mainRelease.Name).EndsWith("/"+mainRelease.Name),"published release staging cannot alias an installed image with the release filename");
foreach(string name in new[]{"../AnimalTown-0.11.24.nkit.iso","AnimalTown-../Town.nkit.iso","gcloader:/AnimalTown-0.11.24.nkit.iso","AnimalTown-0.11.24.iso","AnimalTown-0.11.24.nkit.iso.bak"})Reject(()=>colliding.DownloadPath(name),"release download names cannot escape the staging directory");
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
foreach(string target in new[]{"gcloader:/My Town.iso","gcloader:/Games/Animal Town/custom.nkit.iso","gcloader:/街/Forêt 🦊.gcm"})foreach(bool after in new[]{false,true})for(int failure=1;failure<=5;failure++){
    var request=LaunchRequest.Parse(Request(target));string staged=request.DownloadPath(mainRelease.Name);
    var files=new FakeFiles{FailAt=failure,FailAfter=after};files.Data[request.Image]="original";files.Data[request.Backup]="older";files.Data[staged]="new";files.Data["gcloader:/AnimalTown.iso"]="unrelated";
    try{InstallTransaction.Commit(files,request.Image,staged);}catch(IOException){}
    files.FailAt=0;InstallTransaction.Recover(files,request.Image);InstallTransaction.Recover(files,request.Image);
    Check(files.Data[request.Image]=="original"||files.Data[request.Image]=="new"&&files.Data[request.Backup]=="original","arbitrary target paths retain a complete original or replacement at every install fault");
    Check(files.Data["gcloader:/AnimalTown.iso"]=="unrelated"&&!files.Exists("gcloader:/AnimalTown.iso.update")&&!files.Exists("gcloader:/AnimalTown.iso.bak"),"dynamic target recovery never touches a hardcoded root image");
}
string temporary=Path.Combine(Path.GetTempPath(),"animaltown-updater-"+Guid.NewGuid());Directory.CreateDirectory(temporary);
try{
    string path=Path.Combine(temporary,image);var header=new byte[0x440];Encoding.ASCII.GetBytes("AMTE01").CopyTo(header,0);new byte[]{0xc2,0x33,0x9f,0x3d}.CopyTo(header,0x1c);Encoding.ASCII.GetBytes("AnimalTown / "+release.Version).CopyTo(header,0x20);File.WriteAllBytes(path,header);
    Check(ImageFiles.ReadVersion(path)==release.Version,"standalone updater reads installed version from ISO header");
    foreach(string version in new[]{"0.11.24","0.11.24+abcdef123456"}){
        Array.Clear(header,0x20,header.Length-0x20);Encoding.ASCII.GetBytes("AnimalTown / "+version).CopyTo(header,0x20);File.WriteAllBytes(path,header);
        var request=LaunchRequest.Parse(Request("gcloader:/Games/My Town.nkit.iso",version));request.VerifyInstalledVersion(ImageFiles.ReadVersion(path));
        Check(ImageFiles.ReadVersion(path)==version,"numeric and review identities are verified against the actual image header");
        var staleRequest=LaunchRequest.Parse(Request("gcloader:/Games/My Town.nkit.iso","0.11.23"));Reject(()=>staleRequest.VerifyInstalledVersion(ImageFiles.ReadVersion(path)),"changed header version is rejected using actual storage content");
    }
    var content=new byte[196777];for(int i=0;i<content.Length;i++)content[i]=(byte)(i%251);File.WriteAllBytes(path,content);
    Check(await ImageFiles.HashAsync(path,default)==Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant(),"yielding SD verification hashes multiple slices");
    using var cancelled=new CancellationTokenSource();cancelled.Cancel();bool interrupted=false;
    try{await ImageFiles.HashAsync(path,cancelled.Token);}catch(OperationCanceledException){interrupted=true;}
    Check(interrupted,"SD verification observes cancellation");
    header[0]=0;File.WriteAllBytes(path,header);Reject(()=>ImageFiles.ReadVersion(path),"foreign image preserved");
}finally{Directory.Delete(temporary,true);}
if(args.Length==1){var live=ReleaseInfo.Parse(File.ReadAllText(args[0]));Check(live.Name.EndsWith(".nkit.iso"),"actual GitHub release metadata");Console.WriteLine("Live release parsed: "+live.Name);}
Console.WriteLine(checks+" updater checks passed: request paths, current/legacy identities, SHA-256, release selection and interrupted installation.");

sealed class FakeFiles : IInstallFiles
{
    internal readonly Dictionary<string,string> Data=new();internal int FailAt,FailAgain;internal bool FailAfter;private int mutations;
    private void Mutate(){mutations++;if(!FailAfter&&(mutations==FailAt||mutations==FailAgain))throw new IOException("Injected I/O failure");}
    private void After(){if(FailAfter&&(mutations==FailAt||mutations==FailAgain))throw new IOException("Injected post-mutation I/O failure");}
    public bool Exists(string p)=>Data.ContainsKey(p);
    public void Move(string a,string b){Mutate();if(!Data.ContainsKey(a)||Data.ContainsKey(b))throw new IOException();Data[b]=Data[a];Data.Remove(a);After();}
    public void Delete(string p){Mutate();Data.Remove(p);After();}
    public void Mark(string p){Mutate();Data.Add(p,"ATUPDATE1");After();}
    public bool HasMarker(string p)=>Data[p]=="ATUPDATE1";
}
