using System;
namespace AnimalTown.Updater;

internal static class ConsoleText
{
    internal static int Dots(int frame)
    {
        switch(frame%6){case 1:return 1;case 2:return 3;case 3:return 7;case 4:return 6;case 5:return 4;default:return 0;}
    }
    internal static int Percent(long received,long total)=>total<=0?0:(int)(Math.Max(0,Math.Min(total,received))*100/total);
    internal static string MiB(long bytes)
    {
        long tenths=(Math.Max(0,bytes)*10+524288)/1048576;
        return tenths/10+"."+tenths%10;
    }
    internal static string Transfer(long received,long total)=>MiB(received)+" MiB / "+MiB(total)+" MiB";
    // Wrap actual proportional IPL advances; hard-break long paths/filenames.
    // The caller supplies a fixed row budget, keeping every glyph in safe area.
    internal static int Wrap(string text,float width,Func<string,float> measure,string[] rows)
    {
        int at=0,count=0;
        while(at<text.Length&&count<rows.Length){
            while(at<text.Length&&(text[at]==' '||text[at]=='\r'||text[at]=='\n'))at++;
            if(at==text.Length)break;
            int start=at,space=-1;
            while(at<text.Length&&text[at]!='\n'){
                if(measure(text.Substring(start,at-start+1))>width)break;
                if(text[at]==' ')space=at;at++;
            }
            if(at==start)throw new ArgumentException("Safe area is narrower than a font glyph");
            int end=at;
            if(at<text.Length&&text[at]!='\n'&&space>start){end=space;at=space+1;}
            while(end>start&&text[end-1]==' ')end--;
            rows[count++]=text.Substring(start,end-start);
        }
        if(count==0){rows[0]="";return 1;}
        if(at<text.Length){
            string last=rows[count-1];while(last.Length>0&&measure(last+"...")>width)last=last.Substring(0,last.Length-1);
            rows[count-1]=last+"...";
        }
        return count;
    }
}
