using System;

// Read only the loader's standard libogc argv. Paths are interpreted and
// verified by the caller; missing provenance never triggers a filename search.
internal static class NativeLaunchArguments
{
    internal static string[] Read()
    {
        var packed=new byte[4096];int length=Copy(packed);
        if(length<=0)return new string[0];
        if(length>packed.Length)throw new InvalidOperationException("Invalid native launch arguments");
        int count=0;for(int i=0;i<length;i++)if(packed[i]==0)count++;
        var result=new string[count];int start=0,index=0;
        for(int i=0;i<length;i++)if(packed[i]==0){result[index++]=Dolphin.Text.Encoding.UTF8.GetString(packed,start,i-start);start=i+1;}
        return result;
    }
    internal static int Copy(byte[] destination)=>throw new PlatformNotSupportedException("Native libogc launch arguments required");
}
