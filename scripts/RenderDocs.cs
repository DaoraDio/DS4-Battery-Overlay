using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

// Renders the real UI and artwork with demonstration data; no HID or RTSS session starts.
static class RenderDocs
{
    const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
    static Assembly app;
    static Type Type(string name) { return app.GetType(name, true); }
    static object Call(string type, string method, params object[] arguments)
    { return Type(type).GetMethod(method, All).Invoke(null, arguments); }
    static void Set(object target, string field, object value)
    { target.GetType().GetField(field, All).SetValue(target, value); }
    static Graphics GraphicsFor(Bitmap image)
    {
        Graphics graphics = Graphics.FromImage(image);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        return graphics;
    }
    static void Text(Graphics graphics, string text, float size, FontStyle style, Color color, float x, float y)
    {
        using (var font = new Font("Segoe UI", size, style, GraphicsUnit.Pixel))
        using (var brush = new SolidBrush(color)) graphics.DrawString(text, font, brush, x, y);
    }
    static void Rounded(Graphics graphics, RectangleF bounds, float radius, Color color)
    {
        using (var path = new GraphicsPath())
        using (var brush = new SolidBrush(color))
        {
            float diameter = radius * 2;
            path.AddArc(bounds.X, bounds.Y, diameter, diameter, 180, 90);
            path.AddArc(bounds.Right-diameter, bounds.Y, diameter, diameter, 270, 90);
            path.AddArc(bounds.Right-diameter, bounds.Bottom-diameter, diameter, diameter, 0, 90);
            path.AddArc(bounds.X, bounds.Bottom-diameter, diameter, diameter, 90, 90);
            path.CloseFigure(); graphics.FillPath(brush, path);
        }
    }

    [STAThread]
    static int Main(string[] args)
    {
        try
        {
            app = Assembly.LoadFrom(Path.GetFullPath(args[0]));
            string root = Path.GetFullPath(args[1]);
            string previews = Path.Combine(root, "artifacts", "docs", "raw");
            string images = Path.Combine(root, "docs", "images");
            Directory.CreateDirectory(images);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Call("StudioWindow", "RenderPreview", previews);
            string[] raw = {"studio-0", "studio-widget", "studio-1", "studio-2", "studio-3", "studio-4"};
            string[] names = {"overlay", "widget", "popups", "lightbar", "events", "setup"};
            for (int index = 0; index < names.Length; index++)
            using (var original = new Bitmap(Path.Combine(previews, raw[index]+".png")))
            using (var cropped = original.Clone(new Rectangle(8,31,original.Width-16,original.Height-39), PixelFormat.Format32bppArgb))
                cropped.Save(Path.Combine(images,names[index]+".png"), ImageFormat.Png);

            using (var hero = new Bitmap(1440,1080))
            using (var graphics = GraphicsFor(hero))
            using (var background = new LinearGradientBrush(new Rectangle(0,0,1440,1080),Color.FromArgb(15,19,31),Color.FromArgb(40,30,65),35f))
            using (var mark = new Bitmap(Path.Combine(root,"assets","app-mark.png")))
            using (var screenshot = new Bitmap(Path.Combine(images,"overlay.png")))
            {
                graphics.FillRectangle(background,0,0,1440,1080);
                graphics.DrawImage(mark, new Rectangle(80,48,72,72));
                Text(graphics,"DS4 Battery Overlay",40,FontStyle.Bold,Color.FromArgb(244,242,255),170,43);
                Text(graphics,"Version 1  /  Controller battery, in game and on your desktop",21,FontStyle.Regular,Color.FromArgb(178,172,203),173,102);
                Rounded(graphics,new RectangleF(73,170,1294,834),18,Color.FromArgb(70,0,0,0));
                graphics.DrawImage(screenshot,new Rectangle(80,170,1280,824));
                Text(graphics,"RTSS integration    ·    Desktop widget    ·    Custom notifications    ·    Lightbar controls",20,FontStyle.Regular,Color.FromArgb(196,180,238),80,1020);
                hero.Save(Path.Combine(images,"hero.png"),ImageFormat.Png);
            }

            using (var showcase = new Bitmap(1320,540))
            using (var graphics = GraphicsFor(showcase))
            {
                graphics.Clear(Color.FromArgb(16,20,32));
                Text(graphics,"A small overlay. Your style.",32,FontStyle.Bold,Color.FromArgb(246,243,255),40,25);
                Text(graphics,"Artwork rendered by the app, including controller and charging notifications.",18,FontStyle.Regular,Color.FromArgb(166,172,194),42,75);
                int[] levels = {100,75,50,25,5};
                for (int index=0; index<levels.Length; index++)
                {
                    int x=40+index*252;
                    Rounded(graphics,new RectangleF(x,125,232,135),16,Color.FromArgb(29,35,52));
                    object appearance=Activator.CreateInstance(Type("OverlayAppearance"),true);
                    Set(appearance,"Scale",250); Set(appearance,"ShowBackground",false);
                    Set(appearance,"SegmentedIcon",true); Set(appearance,"ShowNumber",true);
                    using (var badge=(Bitmap)Call("BatteryPublisher","CreateBadge",levels[index]+"%",appearance,index==0))
                        graphics.DrawImageUnscaled(badge,x+(232-badge.Width)/2,147);
                    Text(graphics,index==0?"Charging":"Battery at "+levels[index]+"%",16,FontStyle.Regular,Color.FromArgb(184,191,210),x+20,225);
                }
                string[] labels={"Connected","Disconnected","Low battery"};
                int[] events={1,2,3};
                for(int index=0;index<events.Length;index++)
                {
                    int x=40+index*424;
                    Text(graphics,labels[index],18,FontStyle.Bold,Color.FromArgb(200,185,242),x,307);
                    using(var popup=(Bitmap)Call("ControllerNotifications","TestImage",150,events[index],true))
                        graphics.DrawImageUnscaled(popup,x,348);
                }
                Text(graphics,"Five bars or continuous fill  ·  Optional percentage  ·  Transparent or rounded badge",18,FontStyle.Regular,Color.FromArgb(166,172,194),42,491);
                showcase.Save(Path.Combine(images,"overlays.png"),ImageFormat.Png);
            }
            return 0;
        }
        catch(Exception error) { Console.Error.WriteLine(error);return 1; }
    }
}
