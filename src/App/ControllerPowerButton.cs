using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

sealed class ControllerPowerButton : Button
{
    bool hovered;
    public ControllerPowerButton()
    {
        AccessibleName="Turn off controller";
        AccessibleRole=AccessibleRole.PushButton;
        FlatStyle=FlatStyle.Flat;
        FlatAppearance.BorderSize=0;
        Cursor=Cursors.Hand;
        SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer,true);
    }
    protected override void OnMouseEnter(System.EventArgs e){hovered=true;base.OnMouseEnter(e);Invalidate();}
    protected override void OnMouseLeave(System.EventArgs e){hovered=false;base.OnMouseLeave(e);Invalidate();}
    protected override void OnPaint(PaintEventArgs e)
    {
        var graphics=e.Graphics;
        graphics.Clear(StudioTheme.Card);
        graphics.SmoothingMode=SmoothingMode.AntiAlias;
        float scale=System.Math.Min(Width,Height)/30f;
        var bounds=new RectangleF(0.75f,0.75f,Width-1.5f,Height-1.5f);
        float radius=5*scale;
        using(var shape=new GraphicsPath())
        {
            shape.AddArc(bounds.Left,bounds.Top,2*radius,2*radius,180,90);
            shape.AddArc(bounds.Right-2*radius,bounds.Top,2*radius,2*radius,270,90);
            shape.AddArc(bounds.Right-2*radius,bounds.Bottom-2*radius,2*radius,2*radius,0,90);
            shape.AddArc(bounds.Left,bounds.Bottom-2*radius,2*radius,2*radius,90,90);
            shape.CloseFigure();
            if(hovered&&Enabled)using(var fill=new SolidBrush(StudioTheme.Selection))graphics.FillPath(fill,shape);
            using(var border=new Pen(hovered&&Enabled?StudioTheme.Accent:StudioTheme.Border))graphics.DrawPath(border,shape);
        }
        Color color=Enabled?(hovered?StudioTheme.Accent:StudioTheme.Text):Color.FromArgb(84,94,114);
        float x=Width/2f,y=Height/2f+scale;
        using(var pen=new Pen(color,1.7f*scale){StartCap=LineCap.Round,EndCap=LineCap.Round})
        {
            graphics.DrawArc(pen,x-6*scale,y-6*scale,12*scale,12*scale,-45,270);
            graphics.DrawLine(pen,x,y-8*scale,x,y-1*scale);
        }
        if(Focused&&ShowFocusCues)ControlPaint.DrawFocusRectangle(graphics,Rectangle.Inflate(ClientRectangle,-4,-4),StudioTheme.Accent,StudioTheme.Card);
    }
}
