using System;
using System.IO;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
class MakeIcon
{
    static GraphicsPath Round(float x, float y, float w, float h, float r) {
        var p = new GraphicsPath(); p.AddArc(x,y,r,r,180,90); p.AddArc(x+w-r,y,r,r,270,90); p.AddArc(x+w-r,y+h-r,r,r,0,90); p.AddArc(x,y+h-r,r,r,90,90); p.CloseFigure(); return p;
    }
    static void Main(string[] args) {
        int[] sizes = {16,24,32,48,64,128,256}; byte[][] images = new byte[sizes.Length][];
        for(int i=0;i<sizes.Length;i++) using(var bitmap = new Bitmap(sizes[i],sizes[i])) {
            using(var g = Graphics.FromImage(bitmap)) {
                g.SmoothingMode = SmoothingMode.AntiAlias; g.Clear(Color.Transparent); g.ScaleTransform(sizes[i]/64f,sizes[i]/64f);
                using(var shape=Round(2,2,60,60,16)) using(var fill=new LinearGradientBrush(new Point(0,0),new Point(64,64),Color.FromArgb(40,190,154),Color.FromArgb(15,104,98))) g.FillPath(fill,shape);
                using(var pin=new Pen(Color.FromArgb(196,218,255),3)) {
                    for(int n=21;n<=41;n+=10) {g.DrawLine(pin,n,12,n,18);g.DrawLine(pin,n,46,n,52);g.DrawLine(pin,12,n,18,n);g.DrawLine(pin,46,n,52,n);}
                }
                using(var body=Round(17,17,30,30,6)) using(var white=new SolidBrush(Color.White)) g.FillPath(white,body);
                using(var core=Round(23,23,18,18,4)) using(var fill=new SolidBrush(Color.FromArgb(17,130,117))) g.FillPath(fill,core);
                PointF[] star={new PointF(47,27),new PointF(50,35),new PointF(58,38),new PointF(50,41),new PointF(47,49),new PointF(44,41),new PointF(36,38),new PointF(44,35)};
                using(var pen=new Pen(Color.FromArgb(17,130,117),3))g.DrawPolygon(pen,star);
                using(var fill=new SolidBrush(Color.FromArgb(116,244,217)))g.FillPolygon(fill,star);
            }
            using(var stream=new MemoryStream()) {bitmap.Save(stream,ImageFormat.Png);images[i]=stream.ToArray();}
        }
        Directory.CreateDirectory(Path.GetDirectoryName(args[0]));
        using(var writer=new BinaryWriter(File.Create(args[0]))) {
            writer.Write((ushort)0);writer.Write((ushort)1);writer.Write((ushort)sizes.Length);int offset=6+16*sizes.Length;
            for(int i=0;i<sizes.Length;i++) {writer.Write((byte)(sizes[i]%256));writer.Write((byte)(sizes[i]%256));writer.Write((byte)0);writer.Write((byte)0);writer.Write((ushort)1);writer.Write((ushort)32);writer.Write(images[i].Length);writer.Write(offset);offset+=images[i].Length;}
            foreach(var bytes in images)writer.Write(bytes);
        }
    }
}
