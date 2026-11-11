using System;
using System.IO;
using Dolphin.Serialization.Json;

namespace AnimalTown.Updater;

internal sealed class ReleaseInfo
{
    internal string Version="",Name="",Url="",Digest="";
    internal int Size;
    internal static ReleaseInfo Parse(string json)
    {
        var r=new JsonReader(json);Need(r,JsonTokenType.StartArray);Need(r,JsonTokenType.StartObject);
        string tag="";bool draft=true;var assets=new Dolphin.Collections.List<ReleaseInfo>();
        while(r.Read()&&r.TokenType!=JsonTokenType.EndObject){
            if(r.TokenType!=JsonTokenType.PropertyName)throw new FormatException("Invalid GitHub release");string key=r.Value;
            if(!r.Read())throw new FormatException();
            if(key=="tag_name")tag=Text(r);
            else if(key=="draft")draft=r.TokenType!=JsonTokenType.False;
            else if(key=="assets"){
                if(r.TokenType!=JsonTokenType.StartArray)throw new FormatException();
                while(r.Read()&&r.TokenType!=JsonTokenType.EndArray){
                    if(r.TokenType!=JsonTokenType.StartObject)throw new FormatException();var asset=new ReleaseInfo();
                    while(r.Read()&&r.TokenType!=JsonTokenType.EndObject){
                        string property=r.Value;if(r.TokenType!=JsonTokenType.PropertyName||!r.Read())throw new FormatException();
                        if(property=="name")asset.Name=Text(r);else if(property=="browser_download_url")asset.Url=Text(r);
                        else if(property=="size")asset.Size=r.GetInt32();else if(property=="digest"&&r.TokenType==JsonTokenType.String)asset.Digest=Text(r);else Skip(r);
                    }
                    assets.Add(asset);
                }
            }else Skip(r);
        }
        Need(r,JsonTokenType.EndArray);if(r.Read())throw new FormatException();
        if(draft||tag.Length<2||tag[0]!='v')throw new IOException("No published AnimalTown release");string version=tag.Substring(1);ParseVersion(version);
        ReleaseInfo? result=null;
        for(int i=0;i<assets.Count;i++)if(assets[i].Name=="AnimalTown-"+version+".nkit.iso"){
            if(result!=null)throw new IOException("Duplicate NKit asset");result=assets[i];
        }
        if(result==null||result.Size<0x500||!result.Url.StartsWith("https://github.com/capz/AnimalTown/releases/download/"+tag+"/")||!result.Digest.StartsWith("sha256:")||result.Digest.Length!=71)throw new IOException("Release lacks a verified NKit ISO");
        for(int i=7;i<result.Digest.Length;i++)if(Hex(result.Digest[i])<0)throw new IOException("Invalid release SHA-256");
        result.Digest=result.Digest.Substring(7).ToLowerInvariant();result.Version=version;return result;
    }
    internal static bool NeedsUpdate(string local,string latest)
    {
        ParseVersion(local);ParseVersion(latest);
        // GitHub's newest release is authoritative. First-parent counters can
        // decrease when a branch release is later squash-merged into main;
        // neither those counters nor the commit hash form a total ordering.
        return local!=latest;
    }
    internal static int[] ParseVersion(string text)
    {
        var values=new int[3];int at=0;
        for(int part=0;part<3;part++){
            int start=at,n=0;while(at<text.Length&&text[at]>='0'&&text[at]<='9'){int d=text[at++]-'0';if(n>(int.MaxValue-d)/10)throw new FormatException("Version counter overflow");n=n*10+d;}
            if(at==start||at>=text.Length||text[at++]!='.')throw new FormatException("Invalid AnimalTown version");values[part]=n;
        }
        int remaining=text.Length-at;if(remaining!=12&&remaining!=40)throw new FormatException("Invalid build identity");for(;at<text.Length;at++)if(Hex(text[at])<0)throw new FormatException("Invalid build identity");return values;
    }
    internal static int Hex(char c)=>c>='0'&&c<='9'?c-'0':c>='a'&&c<='f'?c-'a'+10:c>='A'&&c<='F'?c-'A'+10:-1;
    private static string Text(JsonReader r){if(r.TokenType!=JsonTokenType.String)throw new FormatException();return r.Value;}
    private static void Need(JsonReader r,JsonTokenType token){if(!r.Read()||r.TokenType!=token)throw new FormatException("Invalid GitHub release response");}
    private static void Skip(JsonReader r)
    {
        if(r.TokenType!=JsonTokenType.StartArray&&r.TokenType!=JsonTokenType.StartObject)return;
        int depth=1;while(depth>0&&r.Read()){if(r.TokenType==JsonTokenType.StartArray||r.TokenType==JsonTokenType.StartObject)depth++;else if(r.TokenType==JsonTokenType.EndArray||r.TokenType==JsonTokenType.EndObject)depth--;}
        if(depth!=0)throw new FormatException();
    }
}
