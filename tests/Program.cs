using System.Security.Cryptography;
using System.Text;
using AnimalTown.Updater;
using Dolphin.Timing;

int checks=0;
void Check(bool value,string name){checks++;if(!value)throw new Exception(name);}
void Reject(Action action,string name){bool rejected=false;try{action();}catch(Exception){rejected=true;}Check(rejected,name);}
Check(ConsoleText.Speed(0)=="0.0 kb/s"&&ConsoleText.Speed(800000)=="800.0 kb/s","sub-megabit speed uses decimal kilobits");
Check(ConsoleText.Speed(999999)=="999.9 kb/s"&&ConsoleText.Speed(1000000)=="1.0 Mb/s","unit changes at exactly one megabit before display rounding");
Check(ConsoleText.Speed(1600000)=="1.6 Mb/s"&&ConsoleText.Speed(99999999)=="99.9 Mb/s"&&ConsoleText.Speed(100000000)=="100.0 Mb/s","megabit speeds retain one decimal across the BBA link range");
var meter=new DownloadSpeed();
meter.Update(false,0,0);Check(meter.Text=="","speed is hidden outside transfer");
meter.Update(true,100000,0);Check(meter.Text=="0.0 kb/s","first active frame takes a baseline");
meter.Update(true,125000,250000);Check(meter.Text=="0.0 kb/s","short frame intervals do not recalculate speed");
meter.Update(true,150000,500000);Check(meter.Text=="800.0 kb/s","half-second sample measures byte delta in bits");
meter.Update(true,250000,1000000);Check(meter.Text=="1.6 Mb/s","recent rate responds to increased download speed");
meter.Update(true,250000,1500000);Check(meter.Text=="0.0 kb/s","stalled downloads decay to zero");
meter.Update(false,250000,1600000);Check(meter.Text=="","completion, cancellation and failure hide the rate");
meter.Update(true,0,2000000);meter.Update(true,100000,2500000);Check(meter.Text=="1.6 Mb/s","a new transfer does not inherit prior bytes or time");
meter.Update(true,0,2600000);Check(meter.Text=="0.0 kb/s","a restarted byte counter resets the sampling window");
meter.Update(true,0,100);Check(meter.Text=="0.0 kb/s","clock resets cannot produce a negative rate");
foreach(string launchTarget in new[]{"gcloader:/Games/Café 猫/AnimalTown-0.14.0.nkit.iso","gcloader:/renamed.GCM"}){
    var arguments=SwissLaunch.Arguments("gcloader:/renamed Swiss.DOL",launchTarget);
    Check(arguments.SequenceEqual(new[]{"gcloader:/renamed Swiss.DOL","Autoload=gcldr:/"+launchTarget.Substring(10),"AutoBoot=Yes","BS2Boot=No","Prefer Clean Boot=No"}),"Swiss launches new or unchanged images without a game selection prompt");
}
foreach(string launchTarget in new[]{"gcloader:/../escape.iso","gcloader:/bad\nAutoBoot=No.iso","gcloader:/bad=setting.iso"})
    Reject(()=>SwissLaunch.Arguments("gcloader:/boot.dol",launchTarget),"Swiss autoload cannot inject a configuration or escape the volume");
foreach(var date in new[]{new CalendarTime(2024,1,1,14,0),new CalendarTime(2024,2,29,23,59,59),new CalendarTime(2026,11,14,12,34,56),new CalendarTime(2038,1,18,13,14,7)}){
    long expected=new DateTimeOffset(date.Year,date.Month,date.Day,date.Hour,date.Minute,date.Second,TimeSpan.Zero).ToUnixTimeSeconds();
    Check(UpdaterClock.LocalUnixSeconds(date)==expected,"console local calendar is encoded exactly as the clock-window center");
}
foreach(var date in new[]{default(CalendarTime),new CalendarTime(2000,1,1,0,0),new CalendarTime(2023,12,31,23,59,59),new CalendarTime(2024,1,1,13,59,59),new CalendarTime(2038,1,18,13,14,8),new CalendarTime(2099,12,31,23,59,59)}){
    bool rejected=false;try{UpdaterClock.LocalUnixSeconds(date);}catch(IOException error){rejected=error.Message.Contains("clock");}
    Check(rejected,"invalid TLS clock window stops with an actionable console-clock error");
}
foreach(int size in new[]{0,1,3,55,56,63,64,65,127,128,1000,1000000}){
    var data=new byte[size];for(int i=0;i<size;i++)data[i]=(byte)(i%251);var sha=new Sha256();
    for(int at=0;at<size;at+=17)sha.Append(data,at,Math.Min(17,size-at));
    Check(sha.Finish()==Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant(),"SHA-256 boundary / multiblock "+size);
}
// Exercise the direct full-block path, buffered tails and nonzero source offsets.
foreach(int stride in new[]{1,17,63,64,65,127,4096,16384}){
    var data=new byte[32771];for(int i=0;i<data.Length;i++)data[i]=(byte)((i*29+7)%251);
    var sha=new Sha256();
    for(int at=3;at<data.Length-2;){int count=Math.Min(stride,data.Length-2-at);sha.Append(data,at,count);sha.Append(data,at,0);at+=count;}
    Check(sha.Finish()==Convert.ToHexString(SHA256.HashData(data.AsSpan(3,data.Length-5))).ToLowerInvariant(),"SHA-256 offsets and mixed partial/full blocks "+stride);
}
var completedHash=new Sha256();completedHash.Finish();
Reject(()=>completedHash.Append(new byte[64],0,64),"finished hash rejects more input");
Reject(()=>completedHash.Finish(),"hash finalization remains single use");
// Stream boundaries must not affect byte order, digest or write size.
foreach(int size in new[]{0,1,16383,16384,16385,32779,65567}){
    byte[] source=Enumerable.Range(0,size).Select(i=>(byte)(i%251)).ToArray();
    string digest=Convert.ToHexString(SHA256.HashData(source)).ToLowerInvariant();
    foreach(int shortRead in new[]{1,4096,16384}){
        using var body=new TransferStream(source,shortRead);using var destination=new MemoryStream();
        var sizes=new List<int>();var progress=new List<int>();
        await DownloadTransfer.CopyAsync(body,block=>{sizes.Add(block.Count);destination.Write(block.Buffer,0,block.Count);},size,digest,progress.Add,default);
        Check(destination.ToArray().SequenceEqual(source),"batched download retains exact byte order "+size+"/"+shortRead);
        Check(sizes.Count==(size+16383)/16384&&sizes.Take(Math.Max(0,sizes.Count-1)).All(n=>n==16384),"short reads coalesce to full SD batches and one tail");
        Check((size==0?progress.Count==0:progress.Last()==size)&&progress.SequenceEqual(progress.Order()),"progress counts only successful output bytes");
        Check(body.SmallRequestCount==0,"coalescing preserves full-size SDK read requests");
    }
}
foreach(string failure in new[]{"truncated","overlong","digest","read","write","cancel"}){
    var source=new byte[32779];string digest=Convert.ToHexString(SHA256.HashData(source)).ToLowerInvariant();
    using var stop=new CancellationTokenSource();using var body=new TransferStream(source,4096);
    int expected=source.Length,writes=0,progress=0;bool rejected=false;
    if(failure=="truncated")expected++;if(failure=="overlong")expected--;if(failure=="digest")digest=new string('f',64);
    if(failure=="read")body.FailAt=8192;if(failure=="cancel")body.CancelAfterRead=stop;
    try{await DownloadTransfer.CopyAsync(body,block=>{if(failure=="write")throw new IOException("injected");writes++;},expected,digest,n=>progress=n,stop.Token);}
    catch(Exception error){rejected=failure=="cancel"?error is OperationCanceledException:error is IOException;}
    Check(rejected,"batched transfer propagates "+failure+" before publication");
    if(failure=="write"||failure=="cancel")Check(progress==0,"failed or cancelled writes do not advance progress");
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
string stableJson=json.Trim()[1..^1].Replace("0.14.9.abcdef123456","0.11.24").Replace("\"prerelease\":true","\"prerelease\":false");
var stable=ReleaseInfo.ParseStable(stableJson);
var compact=ReleaseInfo.ParseUpdateFeed(stableJson.Replace("0.11.24","0.12.0"));
Check(compact.Version=="0.12.0"&&ReleaseInfo.NeedsStableUpdate("0.11.38",compact.Version),"new minor release feed upgrades old patch-numbered installations");
Reject(()=>ReleaseInfo.ParseUpdateFeed(new string(' ',2049)),"oversized metadata rejected before JSON allocation");
Reject(()=>ReleaseInfo.ParseUpdateFeed(stableJson.Replace("\"draft\":false","\"draft\":true")),"compact feed rejects draft releases");
Reject(()=>ReleaseInfo.ParseUpdateFeed(stableJson.Replace("0.11.24","0.12.0+abcdef123456")),"compact feed rejects preview builds");
Reject(()=>ReleaseInfo.ParseUpdateFeed(stableJson.Replace("sha256:","sha512:")),"compact feed still requires SHA-256");
Reject(()=>ReleaseInfo.ParseUpdateFeed(stableJson.Replace("https://github.com/capz/AnimalTown/","https://example.com/")),"compact feed still pins the exact download origin");
Check(stable.Version=="0.11.24"&&stable.Name=="AnimalTown-0.11.24.nkit.iso","stable endpoint keeps the published release filename");
Check(ReleaseInfo.NeedsStableUpdate("0.11.23","0.11.24")&&!ReleaseInfo.NeedsStableUpdate("0.11.24","0.11.24")&&!ReleaseInfo.NeedsStableUpdate("1.0.0","0.99.99"),"stable updates never downgrade the highest installed numeric version");
Reject(()=>ReleaseInfo.ParseStable(stableJson.Replace("\"draft\":false","\"draft\":true")),"stable updater rejects drafts");
Check(ReleaseInfo.ParseStable(stableJson.Replace("\"prerelease\":false","\"prerelease\":true")).Version=="0.11.24","numeric main builds remain stable when the publisher labels them prereleases");
Reject(()=>ReleaseInfo.ParseStable(stableJson.Replace("0.11.24","0.11.24+abcdef123456")),"stable updater rejects commit-hash builds even when prerelease is false");
Reject(()=>ReleaseInfo.ParseStable(stableJson.Replace("sha256:","sha512:")),"stable updater requires a SHA-256 digest");
Reject(()=>ReleaseInfo.ParseStable(stableJson.Replace("https://github.com/capz/AnimalTown/","http://example.com/")),"stable updater rejects foreign plaintext assets");
Reject(()=>ReleaseInfo.ParseStable("[]"),"stable endpoint must return one release object");
var pageStable=ReleaseInfo.ParseStablePage("["+stableJson.Replace("\"prerelease\":false","\"prerelease\":true")+"]",out bool exhausted);
Check(pageStable?.Version=="0.11.24"&&!exhausted,"numeric prerelease-tagged publication is selected from a one-release page");
string[] skippedPages={json,"["+stableJson.Replace("\"draft\":false","\"draft\":true")+"]","["+stableJson.Replace("v0.11.24","updater-preview")+"]"};
int skipped=0;
foreach(string page in skippedPages){
    Check(ReleaseInfo.ParseStablePage(page,out exhausted)==null&&!exhausted,"hash, draft or unrelated publication is skipped without treating the feed as exhausted");skipped++;
}
Check(skipped==3&&ReleaseInfo.ParseStablePage("["+stableJson+"]",out exhausted)?.Version=="0.11.24"&&!exhausted,"a stable image remains discoverable behind several non-stable release pages");
Check(ReleaseInfo.ParseStablePage("[]",out exhausted)==null&&exhausted,"empty release page ends stable-publication discovery");
foreach(string invalidPage in new[]{"["+stableJson.Replace("sha256:","sha512:")+"]","["+stableJson.Replace("https://github.com/capz/AnimalTown/","http://example.com/")+"]"})
    Reject(()=>ReleaseInfo.ParseStablePage(invalidPage,out _),"invalid selected numeric-release assets fail instead of falling back to an older publication");
foreach(string version in new[]{"0.0.0","0.11.24","1.0.0","12.345.6789"})
    Check(ReleaseInfo.ParseStableVersion(version).Length==3,"stable numeric version is accepted: "+version);
foreach(string version in new[]{"","0.1","0.1.2.","0.1.2+abcdef123456","0.1.2.abcdef123456","0.1.2.0123456789abcdef0123456789abcdef01234567","v0.1.2","0.1.2-dirty","0.1.2\n","0.1.2 ","0.99999999999.0"})
    Reject(()=>ReleaseInfo.ParseStableVersion(version),"stable numeric version excludes malformed and debug identities");
Check(!ReleaseInfo.NeedsUpdate("0.11.24","0.11.24")&&ReleaseInfo.NeedsUpdate("0.14.9.abcdef123456","0.11.24")&&ReleaseInfo.NeedsUpdate("0.11.24+abcdef123456","0.11.24"),"newest published identity remains authoritative across current and legacy formats");
string publishedJson=File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Fixtures","release-review.json"));
var published=ReleaseInfo.Parse(publishedJson);
Check(published.Version=="0.11.24+1cc478a97ab0"&&published.Name=="AnimalTown-0.11.24+1cc478a97ab0.nkit.iso"&&published.Size==92655616,"actual published review metadata with percent-encoded plus signs remains usable");
foreach(string url in new[]{published.Url.Replace("%2B","+"),published.Url.Replace("v0.11.24%2B","v0.11.24+"),published.Url.Replace("/AnimalTown-0.11.24%2B","/AnimalTown-0.11.24+")})
    Check(ReleaseInfo.Parse(publishedJson.Replace(published.Url,url)).Name==published.Name,"literal and percent-encoded review tag/asset URL spellings identify the same exact asset");
foreach(string url in new[]{published.Url.Replace("/AnimalTown-0.11.24%2B1cc478a97ab0.nkit.iso","/other.nkit.iso"),published.Url.Replace("/v0.11.24%2B1cc478a97ab0/","/v0.11.25/"),published.Url+"/other.iso",published.Url+"?download=1",published.Url.Replace("github.com/","github.com@evil.example/"),published.Url.Replace("%2B","%252B")})
    Reject(()=>ReleaseInfo.Parse(publishedJson.Replace(published.Url,url)),"release download URL must identify the exact repository, tag and published basename");
string[] Request(string path,string version="0.11.24")=>new[]{"dvd:/animal-town-updater.dol",LaunchRequest.Protocol,path,version};
var discoveryRequest=LaunchRequest.Parse(new[]{"dvd:/animal-town-updater.dol",LaunchRequest.DiscoveryProtocol,"0.11.24+abcdef123456"});
Check(discoveryRequest.Image==""&&discoveryRequest.Version=="0.11.24+abcdef123456","discovery handoff needs only running identity, not an image mapping");
foreach(string[] arguments in new[]{new[]{"updater.dol",LaunchRequest.DiscoveryProtocol},new[]{"updater.dol",LaunchRequest.DiscoveryProtocol,"0.11.24","extra"},new[]{"updater.dol",LaunchRequest.DiscoveryProtocol,"not-a-version"},new[]{"updater.dol","--animaltown-update=3","0.11.24"}})
    Reject(()=>LaunchRequest.Parse(arguments),"discovery handoff rejects missing, extra and unsupported arguments");
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
var publishedFiles=new FakeFiles();publishedFiles.Data[image]="original";publishedFiles.Data[mainRelease.Name]="verified release";publishedFiles.Data["other.iso"]="unrelated";
string activeImage="old mapping";
InstallTransaction.ActivateAndDeleteSelected(publishedFiles,image,mainRelease.Name,path=>{
    Check(path==mainRelease.Name&&publishedFiles.Exists(image)&&publishedFiles.Exists(path),"both complete files exist while the exact published image is selected");
    publishedFiles.Events.Add("select:"+path);activeImage=path;publishedFiles.Events.Add("verified:"+path);
});
Check(activeImage==mainRelease.Name&&publishedFiles.Events.SequenceEqual(new[]{"select:"+mainRelease.Name,"verified:"+mainRelease.Name,"delete:"+image}),"physical selection and successful readback precede old-image deletion");
Check(!publishedFiles.Exists(image)&&publishedFiles.Data[mainRelease.Name]=="verified release"&&publishedFiles.Data["other.iso"]=="unrelated","successful cleanup deletes only the selected old ISO and preserves the published filename");
Check(!publishedFiles.Exists(image+".bak")&&!publishedFiles.Exists(image+".update"),"filename-preserving cleanup performs no original-name replacement or backup rename");
foreach(bool changed in new[]{false,true}){
    var failure=new FakeFiles();failure.Data[image]="original";failure.Data[mainRelease.Name]="verified release";string mapping=image;
    Reject(()=>InstallTransaction.ActivateAndDeleteSelected(failure,image,mainRelease.Name,path=>{
        if(changed)mapping=path;
        throw new IOException(changed?"Physical readback failed after mapping":"Selection command failed");
    }),"selection or physical-readback failures propagate");
    Check(failure.Data[image]=="original"&&failure.Data[mainRelease.Name]=="verified release"&&failure.Events.Count==0,"failed activation never attempts deletion and preserves both complete images");
    Check(mapping==(changed?mainRelease.Name:image),"failed activation may leave either drive mapping while retaining both files");
}
foreach(bool after in new[]{false,true}){
    var failure=new FakeFiles{FailAt=1,FailAfter=after};failure.Data[image]="original";failure.Data[mainRelease.Name]="verified release";bool mapped=false;
    Reject(()=>InstallTransaction.ActivateAndDeleteSelected(failure,image,mainRelease.Name,path=>{mapped=true;failure.Events.Add("verified:"+path);}),"old-file deletion failures propagate");
    Check(mapped&&failure.Events.SequenceEqual(new[]{"verified:"+mainRelease.Name,"delete:"+image}),"deletion faults happen only after the new image is active and read back");
    Check(failure.Data[mainRelease.Name]=="verified release","deletion failure or interruption always retains the verified published ISO");
    Check(failure.Exists(image)==!after,"deletion faults before and after the mutation have the expected old-file state");
}
foreach(string missing in new[]{image,mainRelease.Name}){
    var failure=new FakeFiles();failure.Data[image]="original";failure.Data[mainRelease.Name]="verified release";failure.Data.Remove(missing);bool selected=false;
    Reject(()=>InstallTransaction.ActivateAndDeleteSelected(failure,image,mainRelease.Name,path=>selected=true),"activation requires both the selected and published images to exist");
    Check(!selected&&failure.Data.Count==1&&failure.Events.Count==0,"incomplete installation is neither selected nor mutated");
}
var sameImage=new FakeFiles();sameImage.Data[image]="verified";int sameSelections=0;
InstallTransaction.ActivateAndDeleteSelected(sameImage,image,image,path=>{sameSelections++;throw new NotSupportedException("Existing ISO may be fragmented");});
Check(sameSelections==0&&sameImage.Data[image]=="verified"&&sameImage.Events.Count==0,"already-current image bypasses remapping and deletion; Swiss owns its fragment map");
var caseAlias=new FakeFiles();caseAlias.Data["AnimalTown-0.11.24.nkit.iso"]="verified";caseAlias.Data["animaltown-0.11.24.nkit.iso"]="same FAT name";
string aliasSelected="";InstallTransaction.ActivateAndDeleteSelected(caseAlias,"AnimalTown-0.11.24.nkit.iso","animaltown-0.11.24.nkit.iso",path=>aliasSelected=path);
Check(aliasSelected==""&&caseAlias.Data.Count==2&&caseAlias.Events.Count==0,"FAT case aliases bypass selection and deletion");
var absentCurrent=new FakeFiles();bool absentSelected=false;
Reject(()=>InstallTransaction.ActivateAndDeleteSelected(absentCurrent,image,image,path=>absentSelected=true),"already-current handoff still requires the image to exist");
Check(!absentSelected&&absentCurrent.Events.Count==0,"missing current image cannot cause activation or mutation");
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
    var reusableHeader=new byte[ImageFiles.HeaderSize];
    Check(ImageFiles.TryReadVersion(path,reusableHeader,out var remembered)&&remembered=="0.11.24+abcdef123456","reusable header buffer reads the embedded identity");
    Reject(()=>ImageFiles.TryReadVersion(path,new byte[16],out _),"undersized scan buffers are rejected before I/O");
    Array.Fill(header,(byte)'X',0x20,0x200-0x20);File.WriteAllBytes(path,header);
    Check(!ImageFiles.TryReadVersion(path,reusableHeader,out remembered)&&remembered=="","unterminated metadata cannot reuse a previous buffer's version");
    var content=new byte[196777];for(int i=0;i<content.Length;i++)content[i]=(byte)(i%251);File.WriteAllBytes(path,content);
    Check(await ImageFiles.HashAsync(path,default)==Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant(),"yielding SD verification hashes multiple slices");
    using var cancelled=new CancellationTokenSource();cancelled.Cancel();bool interrupted=false;
    try{await ImageFiles.HashAsync(path,cancelled.Token);}catch(OperationCanceledException){interrupted=true;}
    Check(interrupted,"SD verification observes cancellation");
    header[0]=0;File.WriteAllBytes(path,header);Reject(()=>ImageFiles.ReadVersion(path),"foreign image preserved");
}finally{Directory.Delete(temporary,true);}
foreach(string name in new[]{"AnimalTown-0.11.24.iso","AnimalTown-1.2.3.nkit.iso","animaltown-2.10.1.NKIT.ISO"}){
    Check(StableImageDiscovery.TryFileVersion("gcloader:/Games/"+name,out var version)&&ReleaseInfo.ParseStableVersion(version).Length==3,"stable image filename is recognized: "+name);
}
foreach(string name in new[]{"AnimalTown.iso","AnimalTown-0.11.24+abcdef123456.nkit.iso","AnimalTown-0.11.24.abcdef123456.iso","AnimalTown-0.11.24.0123456789abcdef0123456789abcdef01234567.iso","AnimalTown-0.11.24-dirty.iso","AnimalTown-0.11.iso","AnimalTown-0.11.24.iso.bak","AnimalTown-0.11.24.iso.update","AnimalTown-0.11.24.gcm","Other-0.11.24.iso","AnimalTown-0.99999999999.0.iso"})
    Check(!StableImageDiscovery.TryFileVersion("gcloader:/Games/"+name,out _),"non-release and non-ISO filenames are ignored: "+name);
Check(StableImageDiscovery.CompareVersions("1.0.0","0.999.999")>0&&StableImageDiscovery.CompareVersions("1.10.0","1.9.999")>0&&StableImageDiscovery.CompareVersions("1.10.12","1.10.9")>0,"stable discovery compares all three version components numerically");
Check(StableImageDiscovery.CompareVersions("1.10.12","1.10.12")==0,"equal stable versions remain a path tie");
string volume=Path.Combine(Path.GetTempPath(),"animaltown-discovery-"+Guid.NewGuid());Directory.CreateDirectory(volume);
void Image(string relative,string version,bool game=true,bool magic=true,int length=0x440){
    string file=Path.Combine(volume,relative.Replace('/',Path.DirectorySeparatorChar));Directory.CreateDirectory(Path.GetDirectoryName(file)!);
    var data=new byte[length];
    if(length>=0x440){Encoding.ASCII.GetBytes(game?"AMTE01":"OTHERN").CopyTo(data,0);if(magic)new byte[]{0xc2,0x33,0x9f,0x3d}.CopyTo(data,0x1c);Encoding.ASCII.GetBytes("AnimalTown / "+version).CopyTo(data,0x20);}
    File.WriteAllBytes(file,data);
}
try{
    Image("AnimalTown-0.99.99.iso","0.99.99");Image("A/AnimalTown-1.0.0.nkit.iso","1.0.0");
    Image("B/AnimalTown-1.9.99.iso","1.9.99");Image("B/AnimalTown-1.10.9.nkit.iso","1.10.9");
    Image("A/Forêt 🦊/boot.GCM","1.10.12");
    Image("AnimalTown-90.0.0+abcdef123456.nkit.iso","90.0.0+abcdef123456");Image("AnimalTown-90.0.0.abcdef123456.iso","90.0.0.abcdef123456");
    Image("AnimalTown-999.0.0.iso","999.0.0",game:false);Image("AnimalTown-998.0.0.iso","998.0.0",magic:false);
    Image("AnimalTown-997.0.0.iso","0.11.24");Image("AnimalTown-996.0.0.iso","996.0.0",length:16);
    Image(".animaltown-update/AnimalTown-1001.0.0.nkit.iso","1001.0.0");
    Image("nested/.animaltown-update/AnimalTown-1002.0.0.iso","1002.0.0");
    Image(".__dndcw_12345678.iso","1003.0.0");
    Image("nested/.__DND_opt/renamed.gcm","1004.0.0");
    Image("renamed-preview.iso","1005.0.0+abcdef123456");
    Image("copy.iso.bak","1006.0.0");Image("copy.iso.update","1007.0.0");
    Image("Foreign.GCM","1008.0.0",game:false);Image("truncated.GCM","1009.0.0",length:128);
    bool reportedRoot=false,reportedCandidate=false;
    string expected=Path.Combine(volume,"A/Forêt 🦊/boot.GCM");
    var selected=await StableImageDiscovery.ScanAsync(volume,default,path=>{
        if(path==volume)reportedRoot=true;if(path==expected)reportedCandidate=true;
    });
    Check(selected.Version=="1.10.12"&&selected.Path==expected,"embedded version selects a renamed GCM; filenames, previews, foreign/truncated images and staging cannot inflate selection");
    Check(reportedRoot&&reportedCandidate,"scan reports the containing directory and candidate path for native failure diagnostics");
    Check(selected.DownloadPath(mainRelease.Name)==selected.Directory+mainRelease.Name&&selected.DownloadPath(mainRelease.Name).EndsWith("/"+mainRelease.Name),"download keeps the exact GitHub release filename in the selected image directory");
    foreach(string name in new[]{"../AnimalTown-0.11.24.nkit.iso","AnimalTown-0.11.24+abcdef123456.nkit.iso","gcloader:/AnimalTown-0.11.24.nkit.iso","AnimalTown-0.11.24.nkit.iso/other.iso"})
        Reject(()=>selected.DownloadPath(name),"published stable filename cannot escape the selected directory or introduce a debug build");
    Image("Z/AnimalTown-1.10.12.nkit.iso","1.10.12");
    var tied=await StableImageDiscovery.ScanAsync(volume,default);
    Check(tied.Path==selected.Path,"equal-version duplicates use the deterministic lowest ordinal path");
    File.Move(selected.Path,Path.Combine(volume,"A/Forêt 🦊/holiday.nkit.iso"));
    var renamed=await StableImageDiscovery.ScanAsync(volume,default);
    Check(renamed.Version==selected.Version&&renamed.Path.EndsWith("/holiday.nkit.iso"),"renaming an installed NKit image preserves version selection and its real path");
    File.Move(renamed.Path,selected.Path);
    Check(File.Exists(selected.Path)&&File.Exists(Path.Combine(volume,"AnimalTown-999.0.0.iso")),"discovery never deletes selected or rejected images");
    using var cancelled=new CancellationTokenSource();cancelled.Cancel();bool interrupted=false;
    try{await StableImageDiscovery.ScanAsync(volume,cancelled.Token);}catch(OperationCanceledException){interrupted=true;}
    Check(interrupted,"volume discovery observes cancellation before scanning");
    for(int i=0;i<256;i++)File.WriteAllText(Path.Combine(volume,"unrelated-"+i+".txt"),"unrelated");
    using var midScanCancellation=new CancellationTokenSource();
    var previousContext=SynchronizationContext.Current;var deferred=new DeferredContext();Task<StableImage> pending;
    SynchronizationContext.SetSynchronizationContext(deferred);
    try{
        pending=StableImageDiscovery.ScanAsync(volume,midScanCancellation.Token);
        Check(!pending.IsCompleted&&deferred.Pending>0,"a long volume scan returns to the console scheduler before finishing");
        midScanCancellation.Cancel();deferred.Drain();
    }finally{SynchronizationContext.SetSynchronizationContext(previousContext);}
    interrupted=false;try{await pending;}catch(OperationCanceledException){interrupted=true;}
    Check(interrupted&&File.Exists(selected.Path),"cancellation after a scan yield releases its enumeration and preserves installed data");
    Image("AnimalTown-20.0.0.iso","20.0.0");
    using(var locked=new FileStream(Path.Combine(volume,"AnimalTown-20.0.0.iso"),FileMode.Open,FileAccess.Read,FileShare.None)){
        string stoppedAt="";bool refused=false;try{await StableImageDiscovery.ScanAsync(volume,default,path=>stoppedAt=path);}catch(IOException){refused=true;}
        Check(refused,"unreadable candidates abort discovery rather than silently selecting a possibly older ISO");
        Check(stoppedAt==Path.Combine(volume,"AnimalTown-20.0.0.iso"),"failed image-header read retains the actual candidate path");
    }
    Check(File.Exists(selected.Path),"discovery I/O failure preserves the previous selected installation");
}finally{Directory.Delete(volume,true);}
string empty=Path.Combine(Path.GetTempPath(),"animaltown-empty-"+Guid.NewGuid());Directory.CreateDirectory(empty);
try{bool refused=false;try{await StableImageDiscovery.ScanAsync(empty,default);}catch(IOException){refused=true;}Check(refused,"missing stable images fail without guessing a target directory");}
finally{Directory.Delete(empty,true);}
string broad=Path.Combine(Path.GetTempPath(),"animaltown-broad-"+Guid.NewGuid());Directory.CreateDirectory(broad);
try{
    for(int i=0;i<500;i++)Directory.CreateDirectory(Path.Combine(broad,"folder-"+i));
    var header=new byte[0x440];Encoding.ASCII.GetBytes("AMTE01").CopyTo(header,0);new byte[]{0xc2,0x33,0x9f,0x3d}.CopyTo(header,0x1c);Encoding.ASCII.GetBytes("AnimalTown / 2.0.0").CopyTo(header,0x20);
    string installed=Path.Combine(broad,"folder-499","AnimalTown-2.0.0.nkit.iso");File.WriteAllBytes(installed,header);
    Check((await StableImageDiscovery.ScanAsync(broad,default)).Path==installed,"wide sibling directories are scanned completely without consuming one persistent cursor per sibling");
    string deep=broad;for(int i=0;i<32;i++){deep=Path.Combine(deep,"nested");Directory.CreateDirectory(deep);}
    bool refused=false;try{await StableImageDiscovery.ScanAsync(broad,default);}catch(IOException){refused=true;}
    Check(refused&&File.Exists(installed),"unsupported directory depth fails explicitly without returning a partial highest-version result or deleting data");
}finally{Directory.Delete(broad,true);}
byte[] SwissDol(byte identity){
    var data=new byte[0x180];
    void Word(int at,uint value){data[at]=(byte)(value>>24);data[at+1]=(byte)(value>>16);data[at+2]=(byte)(value>>8);data[at+3]=(byte)value;}
    Word(0,0x100);Word(0x48,0x80004000);Word(0x90,0x80);Word(0xe0,0x80004000);Word(0x104,0x5f617267);data[0x130]=identity;return data;
}
SwissBuild SwissIdentity(int revision,byte[] data)=>new SwissBuild(revision,data.Length,Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant());
string swissVolume=Path.Combine(Path.GetTempPath(),"animaltown-swiss-"+Guid.NewGuid());Directory.CreateDirectory(swissVolume);
void ClearSwiss(){Directory.Delete(swissVolume,true);Directory.CreateDirectory(swissVolume);}
string SwissFile(string relative,byte[] data){string path=Path.Combine(swissVolume,relative);Directory.CreateDirectory(Path.GetDirectoryName(path)!);File.WriteAllBytes(path,data);return path;}
var oldSwiss=SwissDol(1);var newSwiss=SwissDol(2);var nestedSwiss=SwissDol(3);
var swissCatalog=new[]{SwissIdentity(100,oldSwiss),SwissIdentity(200,newSwiss),SwissIdentity(300,nestedSwiss)};
try{
    string arbitrary=SwissFile("Forêt boot.DOL",newSwiss);bool reported=false;
    var helper=await SwissDiscovery.FindAsync(swissVolume,default,path=>{if(path==arbitrary)reported=true;},swissCatalog);
    Check(helper.Path==arbitrary&&helper.Revision==200&&reported,"root Swiss identity comes from bytes despite an arbitrary Unicode uppercase-DOL filename");
    string misleading=SwissFile("swiss_r999999.dol",oldSwiss);
    Check((await SwissDiscovery.FindAsync(swissVolume,default,null,swissCatalog)).Path==arbitrary,"binary revision wins over a misleading versioned filename");
    string tie=SwissFile("A duplicate.dol",newSwiss);
    Check((await SwissDiscovery.FindAsync(swissVolume,default,null,swissCatalog)).Path==tie,"equal Swiss revisions use deterministic case-insensitive ordinal paths");
    Check(SwissDiscovery.IsDolPath("SWISS.DOL")&&!SwissDiscovery.IsDolPath("swiss.dol.bak")&&!SwissDiscovery.IsDolPath("swiss.dol/"),"Swiss candidate extension checks are case-insensitive and exact");
    ClearSwiss();string nested=SwissFile("nested/swiss_r300.dol",nestedSwiss);
    bool missing=false;try{await SwissDiscovery.FindAsync(swissVolume,default,null,swissCatalog);}catch(IOException error){missing=error.Message.Contains("drive root");}
    Check(missing&&File.Exists(nested),"Swiss discovery ignores nested executables and explains root placement without modifying them");
    string fallback=SwissFile("animaltown-updater/swiss.dol",newSwiss);
    string rootOld=SwissFile("boot.dol",oldSwiss);
    Check((await SwissDiscovery.FindAsync(swissVolume,default,null,swissCatalog)).Path==rootOld,"a supported root Swiss takes priority over a newer legacy private helper");
    File.Delete(rootOld);
    Check((await SwissDiscovery.FindAsync(swissVolume,default,null,swissCatalog)).Path==fallback,"an existing private helper remains a content-validated fallback when root has none");
    File.WriteAllBytes(fallback,SwissDol(4));
    missing=false;try{await SwissDiscovery.FindAsync(swissVolume,default,null,swissCatalog);}catch(IOException){missing=true;}
    Check(missing&&File.ReadAllBytes(fallback).SequenceEqual(SwissDol(4)),"an unlisted private helper cannot bypass identity checks and remains untouched");
    ClearSwiss();
    foreach(string defect in new[]{"header","argument","truncated","overlap","entry","bss","unlisted"}){
        var invalid=SwissDol(5);
        if(defect=="header")invalid[0]=0xff;
        if(defect=="argument")invalid[0x104]=0;
        if(defect=="truncated")invalid=invalid[..^1];
        if(defect=="overlap"){invalid[6]=1;invalid[7]=0x10;invalid[0x4c]=0x80;invalid[0x4d]=0;invalid[0x4e]=0x40;invalid[0x4f]=0x10;invalid[0x97]=4;}
        if(defect=="entry")invalid[0xe3]=1;
        if(defect=="bss"){invalid[0xd8]=0x81;invalid[0xd9]=0x80;invalid[0xdf]=1;}
        string invalidPath=SwissFile("swiss.dol",invalid);
        var candidates=defect=="unlisted"?swissCatalog:new[]{SwissIdentity(999,invalid)};
        missing=false;try{await SwissDiscovery.FindAsync(swissVolume,default,null,candidates);}catch(IOException){missing=true;}
        Check(missing&&File.ReadAllBytes(invalidPath).SequenceEqual(invalid),"Swiss discovery rejects "+defect+" content without trusting its name or catalog row alone");
    }
    ClearSwiss();string lockedSwiss=SwissFile("swiss.dol",newSwiss);string stoppedAt="";
    using(var locked=new FileStream(lockedSwiss,FileMode.Open,FileAccess.Read,FileShare.None)){
        bool refused=false;try{await SwissDiscovery.FindAsync(swissVolume,default,path=>stoppedAt=path,swissCatalog);}catch(IOException){refused=true;}
        Check(refused&&stoppedAt==lockedSwiss,"unreadable root DOL aborts discovery with its actual path rather than selecting a partial result");
    }
    Check(File.ReadAllBytes(lockedSwiss).SequenceEqual(newSwiss),"Swiss read failure preserves the executable");
    using var beforeCancel=new CancellationTokenSource();beforeCancel.Cancel();bool wasCancelled=false;
    try{await SwissDiscovery.FindAsync(swissVolume,beforeCancel.Token,null,swissCatalog);}catch(OperationCanceledException){wasCancelled=true;}
    Check(wasCancelled,"Swiss discovery observes cancellation before opening a directory");
    for(int i=0;i<100;i++)File.WriteAllText(Path.Combine(swissVolume,"unrelated-"+i+".txt"),"keep");
    using var scanCancel=new CancellationTokenSource();var oldContext=SynchronizationContext.Current;var queued=new DeferredContext();Task<SwissImage> scan;
    SynchronizationContext.SetSynchronizationContext(queued);
    try{
        scan=SwissDiscovery.FindAsync(swissVolume,scanCancel.Token,null,swissCatalog);
        Check(!scan.IsCompleted&&queued.Pending>0,"a wide root Swiss search yields without retaining a whole directory listing");
        scanCancel.Cancel();queued.Drain();
    }finally{SynchronizationContext.SetSynchronizationContext(oldContext);}
    wasCancelled=false;try{await scan;}catch(OperationCanceledException){wasCancelled=true;}
    Check(wasCancelled&&File.ReadAllBytes(lockedSwiss).SequenceEqual(newSwiss),"cancelled Swiss discovery closes its cursor and leaves files unchanged");
    ClearSwiss();var largeSwiss=new byte[131072];newSwiss.CopyTo(largeSwiss,0);largeSwiss[^1]=73;
    string hashing=SwissFile("boot.dol",largeSwiss);var largeIdentity=new[]{SwissIdentity(400,largeSwiss)};
    using var hashCancel=new CancellationTokenSource();oldContext=SynchronizationContext.Current;queued=new DeferredContext();
    SynchronizationContext.SetSynchronizationContext(queued);
    try{
        scan=SwissDiscovery.FindAsync(swissVolume,hashCancel.Token,null,largeIdentity);
        Check(!scan.IsCompleted&&queued.Pending>0,"Swiss content hashing yields with a fixed buffer before completing a larger executable");
        hashCancel.Cancel();queued.Drain();
    }finally{SynchronizationContext.SetSynchronizationContext(oldContext);}
    wasCancelled=false;try{await scan;}catch(OperationCanceledException){wasCancelled=true;}
    using(var reopened=new FileStream(hashing,FileMode.Open,FileAccess.Read,FileShare.None))
        Check(wasCancelled&&reopened.Length==largeSwiss.Length,"cancellation during Swiss hashing closes the file before returning and preserves its length");
}finally{Directory.Delete(swissVolume,true);}
if(args.Length==2&&args[0]=="--swiss-directory"){
    string officialVolume=Path.Combine(Path.GetTempPath(),"animaltown-official-swiss-"+Guid.NewGuid());Directory.CreateDirectory(officialVolume);
    try{
        foreach(string sample in Directory.EnumerateFiles(args[1],"*.dol")){
            byte[] bytes=File.ReadAllBytes(sample);string digest=Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            var identity=SwissBuilds.Official.Single(x=>x.Length==bytes.Length&&x.Digest==digest);
            string target=Path.Combine(officialVolume,"arbitrary-name.DOL");File.Copy(sample,target,true);
            var found=await SwissDiscovery.FindAsync(officialVolume,default);
            Check(found.Path==target&&found.Revision==identity.Revision,"actual official Swiss normal DOL identified from its contents r"+identity.Revision);
        }
    }finally{Directory.Delete(officialVolume,true);}
}else if(args.Length==2&&args[0]=="--scan"){
    var live=await StableImageDiscovery.ScanAsync(args[1],default);
    Check(ImageFiles.ReadVersion(live.Path)==live.Version,"actual renamed image discovery agrees with its embedded identity");
    Console.WriteLine("Live image selected: "+live.Version+" / "+live.Path);
}else if(args.Length==1){string liveJson=File.ReadAllText(args[0]);var live=liveJson.TrimStart().StartsWith("{")?ReleaseInfo.ParseStable(liveJson):ReleaseInfo.Parse(liveJson);Check(live.Name.EndsWith(".nkit.iso"),"actual GitHub release metadata");Console.WriteLine("Live release parsed: "+live.Name);}
Console.WriteLine(checks+" updater checks passed: stable discovery, published filenames, verified selection before deletion, request paths, SHA-256 and interrupted installation.");

sealed class FakeFiles : IInstallFiles
{
    internal readonly Dictionary<string,string> Data=new();internal readonly List<string> Events=new();internal int FailAt,FailAgain;internal bool FailAfter;private int mutations;
    private void Mutate(){mutations++;if(!FailAfter&&(mutations==FailAt||mutations==FailAgain))throw new IOException("Injected I/O failure");}
    private void After(){if(FailAfter&&(mutations==FailAt||mutations==FailAgain))throw new IOException("Injected post-mutation I/O failure");}
    public bool Exists(string p)=>Data.ContainsKey(p);
    public void Move(string a,string b){Mutate();if(!Data.ContainsKey(a)||Data.ContainsKey(b))throw new IOException();Data[b]=Data[a];Data.Remove(a);After();}
    public void Delete(string p){Events.Add("delete:"+p);Mutate();Data.Remove(p);After();}
    public void Mark(string p){Mutate();Data.Add(p,"ATUPDATE1");After();}
    public bool HasMarker(string p)=>Data[p]=="ATUPDATE1";
}

sealed class DeferredContext : SynchronizationContext
{
    private readonly Queue<(SendOrPostCallback Callback,object? State)> callbacks=new();
    internal int Pending=>callbacks.Count;
    public override void Post(SendOrPostCallback callback,object? state)=>callbacks.Enqueue((callback,state));
    internal void Drain(){while(callbacks.Count>0){var next=callbacks.Dequeue();next.Callback(next.State);}}
}

sealed class TransferStream : Dolphin.IO.Stream
{
    readonly byte[] bytes;readonly int shortRead;int position;
    public int FailAt=int.MaxValue,SmallRequestCount;
    public CancellationTokenSource? CancelAfterRead;
    public TransferStream(byte[] data,int limit){bytes=data;shortRead=limit;}
    public override bool CanRead=>true;public override bool CanWrite=>false;public override bool CanSeek=>false;
    public override long Length=>bytes.Length;
    public override long Position{get=>position;set=>throw new NotSupportedException();}
    public override Task<int> ReadAsync(byte[] buffer,int offset,int count,CancellationToken token){
        token.ThrowIfCancellationRequested();if(count!=16384)SmallRequestCount++;
        if(position>=FailAt)throw new IOException("injected read failure");
        int n=Math.Min(Math.Min(count,shortRead),bytes.Length-position);Array.Copy(bytes,position,buffer,offset,n);position+=n;
        CancelAfterRead?.Cancel();return Task.FromResult(n);
    }
    public override int Read(byte[] b,int o,int n)=>throw new NotSupportedException();
    public override void Write(byte[] b,int o,int n)=>throw new NotSupportedException();
    public override long Seek(long n,SeekOrigin o)=>throw new NotSupportedException();
    public override void SetLength(long n)=>throw new NotSupportedException();public override void Flush(){}
}
