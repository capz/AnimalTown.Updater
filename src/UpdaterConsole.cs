using System;
using Dolphin.Graphics;
using Dolphin.Timing;
namespace AnimalTown.Updater;

internal sealed class UpdaterConsole : IDisposable
{
    private const float Left=48,Width=544;
    private readonly GraphicsDevice device=GraphicsDevice.Default;
    private readonly IplFont font=IplFont.Load();
    private readonly Draw2D ui=new Draw2D(GraphicsDevice.Default,new Draw2DOptions{Width=640,Height=480,ScaleMode=CanvasScaleMode.Fit,VertexCapacity=8192});
    private readonly string[] lines=new string[5];
    private readonly Func<string,float> measure;
    private readonly float scale,lineHeight,dotCell;
    private readonly long began=Clock.Timestamp;
    private string cachedStatus="",cachedTransfer="",cachedPercent="";
    private int lineCount=1,lastPercent=-1;private long lastReceived=-1,lastTotal=-1;
    private readonly Color32 ink=new Color32(55,60,65),muted=new Color32(100,106,113),rule=new Color32(188,193,199);
    internal UpdaterConsole(){scale=20f/font.CellHeight;lineHeight=Math.Max(24,font.LineHeight*scale);dotCell=font.MeasureText(".",scale).X+3;measure=Measure;}
    private float Measure(string text)=>font.MeasureText(text,scale).X;
    internal void Draw(string status,bool busy,bool failed,int phase,long received,long total,string artifact)
    {
        if(status!=cachedStatus){cachedStatus=status;lineCount=ConsoleText.Wrap(status,Width-dotCell*3-10,measure,lines);}
        int percent=ConsoleText.Percent(received,total);
        if(received!=lastReceived||total!=lastTotal){lastReceived=received;lastTotal=total;cachedTransfer=ConsoleText.Transfer(received,total);}
        if(percent!=lastPercent){lastPercent=percent;cachedPercent=percent+"%";}
        device.BeginFrame(new Color32(232,234,237));ui.Begin();
        Text("ANIMALTOWN",Left,53,16,ink);Right("SYSTEM UPDATE / GCN",Width+Left,57,11,muted);
        ui.FillRectangle(new RectF(Left,87,Width,1),rule);
        float bodyHeight=12+18+lineCount*lineHeight+28+(total>0?64:20);
        float y=Math.Max(111,(480-bodyHeight)/2);
        Text(failed?"UPDATE STOPPED":Stage(phase),Left,y,11,muted);y+=30;
        for(int i=0;i<lineCount;i++)ui.DrawText(font,lines[i]??"",new Vector2(Left,y+i*lineHeight),ink,scale);
        if(busy){
            float x=Left+Measure(lines[lineCount-1]??"")+10,lastY=y+(lineCount-1)*lineHeight;
            int mask=ConsoleText.Dots((int)((Clock.Timestamp-began)/300000%6));
            for(int i=0;i<3;i++)if((mask&(1<<i))!=0)ui.DrawText(font,".",new Vector2(x+i*dotCell,lastY),ink,scale);
        }
        y+=lineCount*lineHeight+28;
        if(total>0){
            Text(cachedPercent,Left,y,28,ink);Right(cachedTransfer,Left+Width,y+12,14,muted);
            ui.FillRectangle(new RectF(Left,y+43,Width,3),rule);
            if(percent>0)ui.FillRectangle(new RectF(Left,y+43,Width*percent/100,3),ink);
            if(artifact.Length>0)Text(artifact,Left,y+57,11,muted);
        }else Text(failed?"Original ISO or backup retained.":"GITHUB RELEASES / NKIT IMAGE",Left,y,12,muted);
        ui.FillRectangle(new RectF(Left,391,Width,1),rule);
        Text(failed?"B  EXIT":"B  CANCEL",Left,409,12,muted);Right("GC LOADER / SD",Left+Width,409,12,muted);
        ui.End();device.EndFrame();
    }
    private void Text(string text,float x,float y,float height,Color32 color)=>ui.DrawText(font,text,new Vector2(x,y),color,height/font.CellHeight);
    private void Right(string text,float right,float y,float height,Color32 color)
    {float size=height/font.CellHeight;ui.DrawText(font,text,new Vector2(right-font.MeasureText(text,size).X,y),color,size);}
    private static string Stage(int phase)
    {
        switch(phase){case 1:return "01 / NETWORK";case 2:return "02 / RELEASE CHECK";case 3:return "03 / TRANSFER";case 4:return "04 / VERIFICATION";case 5:return "05 / INSTALLATION";case 6:return "06 / HANDOFF";default:return "00 / STORAGE";}
    }
    public void Dispose(){ui.Dispose();font.Dispose();}
}
