using System;
using System.IO;

namespace AnimalTown.Updater;

internal static class IsoVersion
{
    internal const string DiscPath="dvd:/version.md";
    internal const int MaximumBytes=64;
    internal static string Read(Stream input)
    {
        long length=input.Length;
        if(length<5||length>MaximumBytes)throw new IOException("ISO version.md is missing or outside its 64-byte limit");
        var bytes=new byte[(int)length];int used=0;
        while(used<bytes.Length){int count=input.Read(bytes,used,bytes.Length-used);if(count==0)throw new IOException("ISO version.md is truncated");used+=count;}
        var text=new char[bytes.Length];
        for(int i=0;i<bytes.Length;i++){
            if(bytes[i]<33||bytes[i]>126)throw new IOException("ISO version.md must contain only the release version, without a BOM or whitespace");
            text[i]=(char)bytes[i];
        }
        string version=new string(text);ReleaseInfo.ParseVersion(version);return version;
    }
}
