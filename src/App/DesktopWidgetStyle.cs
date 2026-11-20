using System.Drawing;
using Microsoft.Win32;

sealed class DesktopWidgetStyle
{
    public int Scale=100, CornerRadius;
    public bool ShowIcon=true, ShowNumber=true, SegmentedIcon=true, NumberInsideIcon;
    public bool ShowBackground=true, ShowDisconnectedIcon;
    public bool HideInGames=true, ClickThrough;
    public Color Background=Color.FromArgb(27,33,41), Text=Color.WhiteSmoke;

    internal DesktopWidgetStyle Copy() { return (DesktopWidgetStyle)MemberwiseClone(); }
    internal OverlayAppearance ToAppearance()
    {
        return new OverlayAppearance {Scale=Scale,CornerRadius=CornerRadius,ShowIcon=ShowIcon,
            ShowNumber=ShowNumber,SegmentedIcon=SegmentedIcon,NumberInsideIcon=NumberInsideIcon,
            ShowBackground=ShowBackground,ShowDisconnectedIcon=ShowDisconnectedIcon,Background=Background,Text=Text};
    }
    internal static DesktopWidgetStyle FromAppearance(OverlayAppearance style,bool hideInGames,bool clickThrough)
    {
        return new DesktopWidgetStyle {Scale=style.Scale,CornerRadius=style.CornerRadius,ShowIcon=style.ShowIcon,
            ShowNumber=style.ShowNumber,SegmentedIcon=style.SegmentedIcon,NumberInsideIcon=style.NumberInsideIcon,
            ShowBackground=style.ShowBackground,ShowDisconnectedIcon=style.ShowDisconnectedIcon,
            Background=style.Background,Text=style.Text,HideInGames=hideInGames,ClickThrough=clickThrough};
    }
    internal static DesktopWidgetStyle Load(RegistryKey key,OverlayAppearance previous)
    {
        // Existing widgets initially retain the appearance they used before separate settings.
        return new DesktopWidgetStyle {
            Scale=OverlayAppearance.Read(key,"WidgetScale",previous.Scale,50,300),
            CornerRadius=OverlayAppearance.Read(key,"WidgetRadius",previous.CornerRadius,0,32),
            ShowIcon=OverlayAppearance.Read(key,"WidgetShowIcon",previous.ShowIcon?1:0,0,1)!=0,
            ShowNumber=OverlayAppearance.Read(key,"WidgetShowNumber",previous.ShowNumber?1:0,0,1)!=0,
            SegmentedIcon=OverlayAppearance.Read(key,"WidgetSegmentedIcon",previous.SegmentedIcon?1:0,0,1)!=0,
            NumberInsideIcon=OverlayAppearance.Read(key,"WidgetNumberInside",previous.NumberInsideIcon?1:0,0,1)!=0,
            ShowBackground=OverlayAppearance.Read(key,"WidgetBackgroundVisible",previous.ShowBackground?1:0,0,1)!=0,
            ShowDisconnectedIcon=OverlayAppearance.Read(key,"WidgetDisconnectedIcon",previous.ShowDisconnectedIcon?1:0,0,1)!=0,
            Background=Color.FromArgb(OverlayAppearance.Read(key,"WidgetBackground",previous.Background.ToArgb(),int.MinValue,int.MaxValue)),
            Text=Color.FromArgb(OverlayAppearance.Read(key,"WidgetText",previous.Text.ToArgb(),int.MinValue,int.MaxValue)),
            HideInGames=OverlayAppearance.Read(key,"WidgetHideInGames",1,0,1)!=0,
            ClickThrough=OverlayAppearance.Read(key,"WidgetClickThrough",0,0,1)!=0
        };
    }
    internal void Save(RegistryKey key)
    {
        key.SetValue("WidgetScale",Scale);key.SetValue("WidgetRadius",CornerRadius);
        key.SetValue("WidgetShowIcon",ShowIcon?1:0);key.SetValue("WidgetShowNumber",ShowNumber?1:0);
        key.SetValue("WidgetSegmentedIcon",SegmentedIcon?1:0);key.SetValue("WidgetNumberInside",NumberInsideIcon?1:0);
        key.SetValue("WidgetBackgroundVisible",ShowBackground?1:0);key.SetValue("WidgetDisconnectedIcon",ShowDisconnectedIcon?1:0);
        key.SetValue("WidgetBackground",Background.ToArgb());key.SetValue("WidgetText",Text.ToArgb());
        key.SetValue("WidgetHideInGames",HideInGames?1:0);key.SetValue("WidgetClickThrough",ClickThrough?1:0);
    }
}
