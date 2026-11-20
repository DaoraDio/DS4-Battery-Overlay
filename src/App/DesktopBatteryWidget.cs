using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

sealed class DesktopBatteryWidget : NativeWindow, IDisposable
{
    [StructLayout(LayoutKind.Sequential)]
    struct BitmapHeader {
        public uint Size; public int Width, Height; public ushort Planes, Bits;
        public uint Compression, ImageSize; public int XPels, YPels; public uint ColorsUsed, Important;
    }
    [StructLayout(LayoutKind.Sequential, Pack=1)]
    struct Blend { public byte Operation, Flags, Opacity, Format; }
    [StructLayout(LayoutKind.Sequential)]
    struct NativeRect { public int Left, Top, Right, Bottom; }
    [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr source);
    [DllImport("gdi32.dll")] static extern IntPtr CreateDIBSection(IntPtr dc, ref BitmapHeader header, uint usage, out IntPtr pixels, IntPtr section, uint offset);
    [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr dc, IntPtr item);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr item);
    [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr dc);
    [DllImport("user32.dll", SetLastError=true)] static extern bool UpdateLayeredWindow(IntPtr window, IntPtr targetDc, ref Point target, ref Size size, IntPtr sourceDc, ref Point source, uint colorKey, ref Blend blend, uint flags);
    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr window, int command);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] static extern bool ReleaseCapture();
    [DllImport("user32.dll")] static extern IntPtr GetShellWindow();
    [DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr window,uint command);
    delegate bool EnumWindow(IntPtr window,IntPtr data);
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumWindow callback,IntPtr data);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern IntPtr FindWindowEx(IntPtr parent,IntPtr after,string name,string title);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")] static extern bool IsWindow(IntPtr window);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr window, int index);
    [DllImport("user32.dll", SetLastError=true)] static extern int SetWindowLong(IntPtr window, int index, int value);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr window, out NativeRect rectangle);

    readonly string preferencesPath;
    Bitmap sourceImage;
    IntPtr dc, dib, previous;
    Size size;
    Point location;
    bool hasPosition, visible, dragging, disposed, clickThrough;
    internal string Error { get; private set; }

    internal DesktopBatteryWidget(string preferencesPath = OverlayAppearance.PreferencesPath)
    {
        this.preferencesPath = preferencesPath;
        try
        {
            using (var key = Registry.CurrentUser.OpenSubKey(preferencesPath))
            {
                object x = key == null ? null : key.GetValue("DesktopWidgetX");
                object y = key == null ? null : key.GetValue("DesktopWidgetY");
                if (x is int && y is int) { location=new Point((int)x,(int)y); hasPosition=true; }
            }
        }
        catch (UnauthorizedAccessException) { }
        catch (System.Security.SecurityException) { }
    }

    internal void Update(Bitmap image, bool enabled, bool gameActive, bool hideInGames=true, bool clickThrough=false)
    {
        if (disposed) return;
        Error=null;
        try
        {
            SetInputMode(clickThrough);
            if (!enabled || image==null) { Hide(); ReleaseSurface(); return; }
            if (gameActive&&hideInGames) { Hide(); return; }
            bool changed=!ReferenceEquals(sourceImage,image) || size!=image.Size;
            if (changed) Prepare(image);
            if (!hasPosition)
            {
                Rectangle area=Screen.PrimaryScreen.WorkingArea;
                location=new Point(area.Right-size.Width-18,area.Bottom-size.Height-18);
                hasPosition=true;
            }
            Point fitted=Constrain(location,size);
            changed |= fitted!=location;
            location=fitted;
            if (Handle==IntPtr.Zero)
            {
                CreateHandle(new CreateParams {
                    Caption="DS4 battery widget", Style=unchecked((int)0x80000000),
                    Parent=FindDesktopOwner(),
                    ExStyle=0x00080000|0x08000000|0x00000080|(this.clickThrough?0x20:0),
                    X=location.X,Y=location.Y,Width=size.Width,Height=size.Height
                });
                changed=true;
            }
            if (changed || !visible)
            {
                var origin=Point.Empty;
                var blend=new Blend {Opacity=255,Format=1};
                if (!UpdateLayeredWindow(Handle,IntPtr.Zero,ref location,ref size,dc,ref origin,0,ref blend,2))
                    throw new Win32Exception(Marshal.GetLastWin32Error());
            }
            if (!visible)
            {
                ShowWindow(Handle,4); // Show without taking focus from the user's app.
                LowerToDesktop();
                visible=true;
            }
        }
        catch (Win32Exception error) { Error="Desktop widget: "+error.Message; Hide(); }
        catch (ArgumentException error) { Error="Desktop widget: "+error.Message; Hide(); }
    }

    static Point Constrain(Point point, Size widgetSize)
    {
        var bounds=new Rectangle(point,widgetSize);
        Rectangle area=Screen.FromRectangle(bounds).WorkingArea;
        return new Point(Math.Max(area.Left,Math.Min(area.Right-widgetSize.Width,point.X)),
            Math.Max(area.Top,Math.Min(area.Bottom-widgetSize.Height,point.Y)));
    }

    static IntPtr FindDesktopOwner()
    {
        IntPtr desktop=IntPtr.Zero;
        EnumWindows(delegate(IntPtr window,IntPtr data) {
            if(FindWindowEx(window,IntPtr.Zero,"SHELLDLL_DefView",null)==IntPtr.Zero)return true;
            desktop=window;return false;
        },IntPtr.Zero);
        return desktop!=IntPtr.Zero?desktop:GetShellWindow();
    }

    void LowerToDesktop()
    {
        if(Handle==IntPtr.Zero)return;
        // Stay immediately above the desktop, so ordinary windows can cover us.
        // HWND_BOTTOM alone can put a layered popup behind its desktop owner.
        IntPtr owner=GetWindow(Handle,4);
        IntPtr above=owner==IntPtr.Zero?IntPtr.Zero:GetWindow(owner,3);
        if(above==Handle)above=GetWindow(Handle,3);
        SetWindowPos(Handle,owner==IntPtr.Zero?new IntPtr(1):above,0,0,0,0,0x0213);
    }

    void ReadWindowPosition()
    {
        NativeRect bounds;
        if(Handle!=IntPtr.Zero&&GetWindowRect(Handle,out bounds))
            location=new Point(bounds.Left,bounds.Top);
    }

    void SetInputMode(bool desired)
    {
        if(clickThrough==desired)return;
        EndDrag();clickThrough=desired;
        if(Handle==IntPtr.Zero)return;
        int style=GetWindowLong(Handle,-20);
        SetWindowLong(Handle,-20,desired?style|0x20:style&~0x20);
        SetWindowPos(Handle,IntPtr.Zero,0,0,0,0,0x0037);
        if(((GetWindowLong(Handle,-20)&0x20)!=0)!=desired)
            throw new Win32Exception(Marshal.GetLastWin32Error());
    }

    void Prepare(Bitmap image)
    {
        ReleaseSurface();
        dc=CreateCompatibleDC(IntPtr.Zero);
        if (dc==IntPtr.Zero) throw new Win32Exception();
        var header=new BitmapHeader {Size=(uint)Marshal.SizeOf(typeof(BitmapHeader)),
            Width=image.Width,Height=-image.Height,Planes=1,Bits=32};
        IntPtr pixels;
        dib=CreateDIBSection(dc,ref header,0,out pixels,IntPtr.Zero,0);
        if (dib==IntPtr.Zero) { ReleaseSurface(); throw new Win32Exception(); }
        previous=SelectObject(dc,dib);
        using (var normalized=new Bitmap(image.Width,image.Height,PixelFormat.Format32bppPArgb))
        {
            using (var graphics=Graphics.FromImage(normalized))
            {
                graphics.CompositingMode=System.Drawing.Drawing2D.CompositingMode.SourceCopy;
                graphics.DrawImageUnscaled(image,0,0);
            }
            var data=normalized.LockBits(new Rectangle(Point.Empty,image.Size),ImageLockMode.ReadOnly,normalized.PixelFormat);
            try
            {
                var row=new byte[image.Width*4];
                for (int y=0;y<image.Height;y++)
                {
                    Marshal.Copy(IntPtr.Add(data.Scan0,y*data.Stride),row,0,row.Length);
                    // Windows ignores mouse input at zero-alpha pixels, even when click-through is off.
                    // A single alpha step keeps the transparent padding usable as a drag target.
                    for(int x=3;x<row.Length;x+=4)if(row[x]==0)row[x]=1;
                    Marshal.Copy(row,0,IntPtr.Add(pixels,y*row.Length),row.Length);
                }
            }
            finally { normalized.UnlockBits(data); }
        }
        size=image.Size;
        sourceImage=image;
    }

    void MoveTo(Point target)
    {
        Point fitted=Constrain(target,size);
        if(fitted==location)return;
        location=fitted;
        if (Handle!=IntPtr.Zero) SetWindowPos(Handle,IntPtr.Zero,location.X,location.Y,0,0,0x0015);
    }

    void SavePosition()
    {
        try
        {
            using (var key=Registry.CurrentUser.CreateSubKey(preferencesPath))
            {
                key.SetValue("DesktopWidgetX",location.X);
                key.SetValue("DesktopWidgetY",location.Y);
            }
        }
        catch (UnauthorizedAccessException) { Error="Could not save the widget position."; }
        catch (System.Security.SecurityException) { Error="Could not save the widget position."; }
    }

    void EndDrag()
    {
        if (!dragging) return;
        dragging=false;
        ReleaseCapture();
        ReadWindowPosition();
        SavePosition();
        LowerToDesktop();
    }

    protected override void WndProc(ref Message message)
    {
        switch (message.Msg)
        {
            case 0x0021: message.Result=new IntPtr(3); return; // MA_NOACTIVATE
            case 0x0084: message.Result=new IntPtr(clickThrough?-1:2); return; // HTCAPTION: native Windows dragging
            case 0x00A3: return; // Do not maximize a widget on double click.
            case 0x0231: dragging=true;return;
            case 0x0232:
                ReadWindowPosition();dragging=false;MoveTo(location);SavePosition();LowerToDesktop();return;
            case 0x0047:
                base.WndProc(ref message);ReadWindowPosition();return;
            case 0x007E: case 0x001A: if (hasPosition) MoveTo(location); break;
        }
        base.WndProc(ref message);
    }

    internal void Hide()
    {
        EndDrag();
        if (visible && Handle!=IntPtr.Zero) ShowWindow(Handle,0);
        visible=false;
    }
    void ReleaseSurface()
    {
        if (previous!=IntPtr.Zero && dc!=IntPtr.Zero) SelectObject(dc,previous);
        if (dib!=IntPtr.Zero) DeleteObject(dib);
        if (dc!=IntPtr.Zero) DeleteDC(dc);
        previous=dib=dc=IntPtr.Zero;sourceImage=null;
    }
    public void Dispose()
    {
        if (disposed) return;
        Hide();ReleaseSurface();
        if (Handle!=IntPtr.Zero) DestroyHandle();
        disposed=true;
    }

    // Exercises a real layered window with isolated preferences; never opens a controller.
    internal static int SelfTest(string report)
    {
        string preferences=OverlayAppearance.PreferencesPath+@"\WidgetQA-"+Guid.NewGuid().ToString("N");
        try
        {
            IntPtr foreground=GetForegroundWindow(),widgetHandle;
            Point saved;
            using (var image=BatteryPublisher.CreateBadge("65%",new OverlayAppearance()))
            using (var widget=new DesktopBatteryWidget(preferences))
            {
                widget.Update(image,false,false);
                if (widget.Handle!=IntPtr.Zero) throw new InvalidOperationException("Disabled widget created a window.");
                widget.Update(image,true,false);
                widgetHandle=widget.Handle;
                if (widget.Error!=null || !IsWindowVisible(widgetHandle)) throw new InvalidOperationException(widget.Error??"Widget not visible.");
                int style=GetWindowLong(widgetHandle,-20);
                if ((style&(0x08000000|0x00080000))!=(0x08000000|0x00080000) || (style&(8|0x20))!=0)
                    throw new InvalidOperationException("Widget cannot accept dragging or could activate.");
                if (GetForegroundWindow()!=foreground) throw new InvalidOperationException("Widget took focus.");
                Point start=widget.location;
                widget.MoveTo(new Point(start.X-40,start.Y-30));
                saved=widget.location;widget.SavePosition();
                if (widget.Error!=null) throw new InvalidOperationException(widget.Error);
                NativeRect actual;
                if (!GetWindowRect(widgetHandle,out actual) || actual.Left!=saved.X || actual.Top!=saved.Y)
                    throw new InvalidOperationException("Widget window did not move.");
                widget.Update(image,true,false,true,true);
                if((GetWindowLong(widgetHandle,-20)&0x20)==0||!IsWindowVisible(widgetHandle))
                    throw new InvalidOperationException("Click-through was not enabled.");
                widget.Update(image,true,false,true,false);
                if((GetWindowLong(widgetHandle,-20)&0x20)!=0||GetForegroundWindow()!=foreground)
                    throw new InvalidOperationException("Click-through could not be disabled without activating.");
                widget.Update(image,true,true,false);
                if(!IsWindowVisible(widgetHandle))throw new InvalidOperationException("Keep visible failed while a game has focus.");
                widget.Update(image,true,true);
                if (IsWindowVisible(widgetHandle)) throw new InvalidOperationException("Widget remained above game.");
                widget.Update(image,true,false);
                if (!IsWindowVisible(widgetHandle) || widget.location!=saved) throw new InvalidOperationException("Widget failed to return.");
                widget.Update(null,true,false);
                if (IsWindowVisible(widgetHandle)) throw new InvalidOperationException("Empty badge left a window.");
                widget.Update(image,true,false);
                widget.Update(image,false,false);
                if (IsWindowVisible(widgetHandle)) throw new InvalidOperationException("Disabling widget failed.");
            }
            if (IsWindow(widgetHandle)) throw new InvalidOperationException("Widget survived app cleanup.");
            using (var restored=new DesktopBatteryWidget(preferences))
                if (!restored.hasPosition || restored.location!=saved) throw new InvalidOperationException(
                    "Widget position was not restored. Saved="+saved+", loaded="+restored.location+", found="+restored.hasPosition);
            using(var key=Registry.CurrentUser.CreateSubKey(preferences))
            {
                var previousStyle=new OverlayAppearance {Scale=125,CornerRadius=8,ShowBackground=false,ShowDisconnectedIcon=true};
                var migrated=DesktopWidgetStyle.Load(key,previousStyle);
                if(migrated.Scale!=125||migrated.CornerRadius!=8||migrated.ShowBackground||!migrated.ShowDisconnectedIcon)
                    throw new InvalidOperationException("Existing widget appearance was not preserved.");
                var custom=new DesktopWidgetStyle {Scale=230,CornerRadius=19,HideInGames=false,ClickThrough=true,
                    Background=Color.Navy,Text=Color.Cyan,ShowNumber=false};
                custom.Save(key);
                var loaded=DesktopWidgetStyle.Load(key,new OverlayAppearance());
                if(loaded.Scale!=230||loaded.CornerRadius!=19||loaded.HideInGames||!loaded.ClickThrough||
                    loaded.Background.ToArgb()!=custom.Background.ToArgb()||loaded.Text.ToArgb()!=custom.Text.ToArgb()||loaded.ShowNumber)
                    throw new InvalidOperationException("Independent widget customization was not saved.");
            }
            System.IO.File.WriteAllText(report,"PASS desktop widget: optional window, layered alpha, desktop layer, no activation, movement, saved position, click-through enabled and disabled, keep visible, game suppression, desktop return, null badge, disabling and cleanup.");
            return 0;
        }
        catch (Exception error) { System.IO.File.WriteAllText(report,error.ToString());return 1; }
        finally { Registry.CurrentUser.DeleteSubKeyTree(preferences,false); }
    }
}
