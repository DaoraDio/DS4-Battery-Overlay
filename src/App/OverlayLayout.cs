using System;
using System.Drawing;
using System.Windows.Forms;

sealed partial class OverlayPage
{
    readonly Button gameTab=StudioTheme.Button("Game overlay");
    readonly Button widgetTab=StudioTheme.Button("Desktop widget");
    readonly StudioSwitch widgetEnabled=new StudioSwitch("Enable desktop widget");
    readonly StudioSwitch widgetClickThrough=new StudioSwitch("Click through widget");
    readonly StudioNumber widgetScale=new StudioNumber(50,300,"widget size");
    readonly StudioNumber widgetRadius=new StudioNumber(0,32,"widget corner radius");
    readonly RadioButton disconnectedHide=ChoiceButton("Show nothing","Hide this display when no controller is connected");
    readonly RadioButton disconnectedShow=ChoiceButton("Controller icon","Show a disconnected controller icon");
    readonly RadioButton hideInGames=ChoiceButton("Hide widget","Hide the desktop widget when a game has focus");
    readonly RadioButton keepInGames=ChoiceButton("Keep visible","Keep the desktop widget when a game has focus");
    readonly Button previewOffline=PreviewButton("Offline","Preview no controller connected");
    readonly Label previewTitle=StudioTheme.Label("In-game preview",12,true);
    readonly Label widgetHelp=StudioTheme.Label("",8);
    readonly RowStyle behaviorHeight=new RowStyle(SizeType.Absolute,142);
    Control gameFields,widgetFields,gameBehavior,widgetBehavior;
    OverlayAppearance draft;
    bool editingWidget,previewDisconnected;

    static TableLayoutPanel GridLayout(int columns,int rows)
    {
        return new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=columns,RowCount=rows,Margin=Padding.Empty};
    }
    static TableLayoutPanel Section(params int[] heights)
    {
        var card=Card(heights.Length);
        foreach (int height in heights)
            if (height<0) card.RowStyles.Add(new RowStyle(SizeType.Percent,100));
            else StudioTheme.FillRow(card,height);
        return card;
    }
    static Control Options(RadioButton first,RadioButton second)
    {
        var row=GridLayout(2,1);
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));
        row.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        row.Controls.Add(first,0,0);row.Controls.Add(second,1,0);
        return row;
    }
    void BuildRedesigned(OverlayAppearance current,Action<OverlayAppearance> apply)
    {
        Dock=DockStyle.Fill;BackColor=StudioTheme.Canvas;
        var root=GridLayout(1,3);
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        StudioTheme.FillRow(root,42);root.RowStyles.Add(new RowStyle(SizeType.Percent,100));StudioTheme.FillRow(root,50);
        Controls.Add(root);

        var navigation=GridLayout(3,1);
        navigation.Margin=new Padding(0,0,0,6);
        navigation.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,166));
        navigation.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,166));
        navigation.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        navigation.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        foreach (var button in new[]{gameTab,widgetTab}) {button.Dock=DockStyle.Fill;button.Margin=new Padding(0,0,6,0);}
        gameTab.Click+=delegate{SwitchTarget(false);};widgetTab.Click+=delegate{SwitchTarget(true);};
        widgetEnabled.Dock=DockStyle.Right;widgetEnabled.Width=194;widgetEnabled.Margin=Padding.Empty;
        widgetEnabled.BackColor=StudioTheme.Canvas;
        navigation.Controls.Add(gameTab,0,0);navigation.Controls.Add(widgetTab,1,0);navigation.Controls.Add(widgetEnabled,2,0);
        root.Controls.Add(navigation,0,0);

        var content=GridLayout(2,1);
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,45));content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,55));
        content.RowStyles.Add(new RowStyle(SizeType.Percent,100));root.Controls.Add(content,0,1);
        var left=GridLayout(1,3);left.Margin=new Padding(0,0,7,0);
        left.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        left.RowStyles.Add(new RowStyle(SizeType.Percent,100));left.RowStyles.Add(behaviorHeight);StudioTheme.FillRow(left,118);
        content.Controls.Add(left,0,0);
        var previewCard=Section(24,34,-1);previewCard.Margin=new Padding(0,0,0,10);
        previewTitle.Dock=DockStyle.Fill;previewTitle.Margin=Padding.Empty;
        previewCard.Controls.Add(previewTitle,0,0);
        previewState.Font=StudioTheme.Font(8);previewState.AutoEllipsis=true;
        previewCard.Controls.Add(PreviewControls(),0,1);
        preview.Dock=DockStyle.Fill;preview.Margin=new Padding(0,5,0,0);previewCard.Controls.Add(preview,0,2);
        left.Controls.Add(previewCard,0,0);

        var behaviorHost=new Panel {Dock=DockStyle.Fill,Margin=Padding.Empty};left.Controls.Add(behaviorHost,0,1);
        var game=Section(24,80);
        game.Controls.Add(FullLabel("Position in game",11,true),0,0);
        positions.Dock=DockStyle.Fill;positions.Margin=Padding.Empty;game.Controls.Add(positions,0,1);
        gameBehavior=game;behaviorHost.Controls.Add(game);
        var widget=Section(24,34,32,36);
        widget.Controls.Add(FullLabel("When a game has focus",11,true),0,0);
        widget.Controls.Add(Options(hideInGames,keepInGames),0,1);
        previewTips.SetToolTip(keepInGames,"Keep the widget on the desktop when a game has focus. App windows can cover it.");
        widgetClickThrough.Dock=DockStyle.Fill;widgetClickThrough.Margin=Padding.Empty;widget.Controls.Add(widgetClickThrough,0,2);
        widgetHelp.Dock=DockStyle.Fill;widgetHelp.Margin=Padding.Empty;widgetHelp.ForeColor=StudioTheme.Muted;widget.Controls.Add(widgetHelp,0,3);
        widgetBehavior=widget;behaviorHost.Controls.Add(widget);
        left.Controls.Add(ChargeDataInfo(),0,2);

        var rightHost=new Panel {Dock=DockStyle.Fill,AutoScroll=true,Margin=new Padding(7,0,0,0)};
        content.Controls.Add(rightHost,1,0);
        var right=new TableLayoutPanel {Dock=DockStyle.Top,Height=462,ColumnCount=1,RowCount=3,Margin=Padding.Empty};
        right.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        foreach (int height in new[]{286,86,90}) StudioTheme.FillRow(right,height);
        rightHost.Controls.Add(right);
        var appearance=Section(24,60,36,36,28,60);appearance.Margin=new Padding(0,0,0,10);
        appearance.Controls.Add(FullLabel("Appearance",12,true),0,0);
        var fieldHost=new Panel {Dock=DockStyle.Fill,Margin=Padding.Empty};
        gameFields=Fields(new[]{"Size (%)","Margin (px)","Radius (px)"},new Control[]{scale,margin,corner});
        widgetFields=Fields(new[]{"Widget size (%)","Widget radius (px)"},new Control[]{widgetScale,widgetRadius});
        fieldHost.Controls.Add(gameFields);fieldHost.Controls.Add(widgetFields);appearance.Controls.Add(fieldHost,0,1);
        appearance.Controls.Add(ChoiceRow(icon,segmented,continuous),0,2);
        appearance.Controls.Add(ChoiceRow(number,outsideIcon,insideIcon),0,3);
        background.Dock=DockStyle.Fill;background.Margin=Padding.Empty;appearance.Controls.Add(background,0,4);
        appearance.Controls.Add(Fields(new[]{"Badge color","Text color"},new Control[]{backgroundColor,textColor}),0,5);
        right.Controls.Add(appearance,0,0);
        var offline=Section(23,34);offline.Padding=new Padding(14,8,14,8);offline.Margin=new Padding(0,0,0,10);
        offline.Controls.Add(FullLabel("When no controller is connected",10,true),0,0);
        offline.Controls.Add(Options(disconnectedHide,disconnectedShow),0,1);right.Controls.Add(offline,0,1);
        var shortcuts=Section(24,34);
        shortcuts.Controls.Add(FullLabel("Toggle shortcut · both displays",10,true),0,0);
        shortcut.Dock=DockStyle.Fill;shortcut.Margin=Padding.Empty;shortcuts.Controls.Add(shortcut,0,1);
        previewTips.SetToolTip(shortcut,"Select the field and press a shortcut. It toggles the game overlay and the desktop widget.");
        right.Controls.Add(shortcuts,0,2);

        var footer=GridLayout(2,1);footer.Margin=new Padding(0,10,0,0);
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,235));
        footer.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        result.Dock=DockStyle.Fill;result.ForeColor=StudioTheme.Muted;result.Margin=Padding.Empty;footer.Controls.Add(result,0,0);
        var actions=new FlowLayoutPanel {Dock=DockStyle.Fill,FlowDirection=FlowDirection.RightToLeft,WrapContents=false,Margin=Padding.Empty};
        var save=StudioTheme.Button("Save changes",true);var reset=StudioTheme.Button("Reset");
        actions.Controls.Add(save);actions.Controls.Add(reset);footer.Controls.Add(actions,1,0);root.Controls.Add(footer,0,2);
        save.Click+=delegate {
            try {apply(Read());result.ForeColor=StudioTheme.Green;result.Text="Overlay and widget settings saved.";}
            catch(Exception error){result.ForeColor=StudioTheme.Warning;result.Text=error.Message;}
        };
        reset.Click+=delegate {
            if(!StudioTheme.Confirm(FindForm(),"Reset display settings","Reset overlay and widget settings?",
                "Restore both displays to their defaults. Select Save changes to keep them.","Reset",new Size(460,155)))return;
            Set(new OverlayAppearance());result.ForeColor=StudioTheme.Muted;result.Text="Defaults restored in preview. Select Save changes to keep them.";
        };
        foreach(var field in new[]{scale,margin,corner,widgetScale,widgetRadius})field.ValueChanged+=delegate{UpdatePreview();};
        foreach(var toggle in new[]{background,widgetEnabled,widgetClickThrough})toggle.CheckedChanged+=delegate{UpdatePreview();};
        positions.PositionChanged+=delegate{UpdatePreview();};
        foreach(var choice in new[]{hideInGames,keepInGames})choice.CheckedChanged+=ChoiceChanged;
        foreach(var choice in new[]{disconnectedHide,disconnectedShow})choice.CheckedChanged+=delegate(object sender,EventArgs args){
            if(readingControls||!((RadioButton)sender).Checked)return;previewDisconnected=true;UpdatePreview();
        };
        backgroundColor.Click+=delegate{if(ChooseColor("Badge background color",ref badgeColor))UpdatePreview();};
        textColor.Click+=delegate{if(ChooseColor("Overlay text color",ref inkColor))UpdatePreview();};
        Set(current);
    }

    void AddOfflinePreview(TableLayoutPanel row)
    {
        row.Controls.Add(previewOffline,2,0);
        previewTips.SetToolTip(previewOffline,"Preview this display without a controller");
        previewOffline.Click+=delegate{previewDisconnected=true;UpdatePreview();};
    }
    internal void SwitchTarget(bool widget)
    {
        if(draft==null)return;
        StoreEditor();editingWidget=widget;LoadEditor();result.Text="";
    }
    void SetDraft(OverlayAppearance settings) {draft=settings.Copy();LoadEditor();}
    void LoadEditor()
    {
        readingControls=true;
        try
        {
            var style=editingWidget?draft.Widget.ToAppearance():draft;
            positions.SelectedPosition=draft.Position;scale.Value=draft.Scale;margin.Value=draft.Margin;corner.Value=draft.CornerRadius;
            widgetScale.Value=draft.Widget.Scale;widgetRadius.Value=draft.Widget.CornerRadius;
            icon.Checked=style.ShowIcon;number.Checked=style.ShowNumber;background.Checked=style.ShowBackground;
            (style.SegmentedIcon?segmented:continuous).Checked=true;
            (style.NumberInsideIcon?insideIcon:outsideIcon).Checked=true;
            (style.ShowDisconnectedIcon?disconnectedShow:disconnectedHide).Checked=true;
            (draft.Widget.HideInGames?hideInGames:keepInGames).Checked=true;
            widgetClickThrough.Checked=draft.Widget.ClickThrough;widgetEnabled.Checked=draft.ShowDesktopWidget;
            behaviorHeight.Height=editingWidget?170:142;
            badgeColor=style.Background;inkColor=style.Text;shortcut.Value=draft.Shortcut;
            gameFields.Visible=gameBehavior.Visible=!editingWidget;
            widgetFields.Visible=widgetBehavior.Visible=widgetEnabled.Visible=editingWidget;
            if(editingWidget){widgetFields.BringToFront();widgetBehavior.BringToFront();}
            else{gameFields.BringToFront();gameBehavior.BringToFront();}
            preview.Centered=editingWidget;previewTitle.Text=editingWidget?"Desktop preview":"In-game preview";
            foreach(var tab in new[]{gameTab,widgetTab}){
                bool active=(tab==widgetTab)==editingWidget;
                tab.BackColor=active?StudioTheme.Selection:StudioTheme.Card;
                tab.FlatAppearance.BorderColor=active?StudioTheme.Accent:StudioTheme.Border;
                tab.ForeColor=active?StudioTheme.Accent:StudioTheme.Text;
            }
        }
        finally{readingControls=false;}
        UpdatePreview();
    }
    OverlayAppearance ReadEditor()
    {
        return new OverlayAppearance {Position=positions.SelectedPosition,Margin=margin.Value,
            Scale=editingWidget?widgetScale.Value:scale.Value,CornerRadius=editingWidget?widgetRadius.Value:corner.Value,
            ShowIcon=icon.Checked,ShowNumber=number.Checked,SegmentedIcon=segmented.Checked,NumberInsideIcon=insideIcon.Checked,
            ShowBackground=background.Checked,Background=badgeColor,Text=inkColor,ShowDisconnectedIcon=disconnectedShow.Checked,
            Shortcut=shortcut.Value};
    }
    void StoreEditor()
    {
        if(readingControls||draft==null)return;
        if(editingWidget)draft.Widget=DesktopWidgetStyle.FromAppearance(ReadEditor(),hideInGames.Checked,widgetClickThrough.Checked);
        else{var next=ReadEditor();next.Widget=draft.Widget.Copy();draft=next;}
        draft.ShowDesktopWidget=widgetEnabled.Checked;draft.Shortcut=shortcut.Value;
    }
    OverlayAppearance ReadDraft(){StoreEditor();return draft.Copy();}
    void RefreshPreview()
    {
        if(readingControls||draft==null)return;
        UpdateSwatch(backgroundColor,badgeColor);UpdateSwatch(textColor,inkColor);
        backgroundColor.Enabled=background.Checked;corner.Enabled=widgetRadius.Enabled=background.Checked;
        UpdateChoice(segmented,icon.Checked);UpdateChoice(continuous,icon.Checked);
        UpdateChoice(outsideIcon,icon.Checked&&number.Checked);UpdateChoice(insideIcon,icon.Checked&&number.Checked);
        foreach(var choice in new[]{disconnectedHide,disconnectedShow,hideInGames,keepInGames})UpdateChoice(choice,true);
        int percent=ReadPreviewPercent();bool sample=previewPercent.HasValue||BatteryPublisher.GetPercent(liveReading??"")<0;
        bool charging=!sample&&liveCharging&&!previewDisconnected;
        previewState.Text=previewDisconnected?(disconnectedShow.Checked?"Offline":"Offline · hidden"):
            (sample?"Sample · ":"Live · ")+percent+"%"+(charging?" · Charging":"");
        previewLive.Visible=previewPercent.HasValue||previewDisconnected;
        previewOffline.BackColor=previewDisconnected?StudioTheme.Selection:StudioTheme.Card;
        previewOffline.FlatAppearance.BorderColor=previewDisconnected?StudioTheme.Accent:StudioTheme.Border;
        widgetHelp.Text=widgetClickThrough.Checked?"Clicks pass to windows behind it. Turn this off to move the widget.":
            "Drag it on the desktop to move it. Other windows appear in front of the widget.";
        preview.UpdateBadge(previewDisconnected?null:sample?percent+"%":liveReading,ReadEditor(),charging);
        result.Text="";
    }

    internal static bool DisconnectedOptionsSelfTest()
    {
        using(var page=new OverlayPage(new OverlayAppearance(),delegate{}))
        {
            if(page.Read().ShowDisconnectedIcon)return false;
            page.disconnectedShow.Checked=true;
            if(!page.Read().ShowDisconnectedIcon||!page.previewDisconnected)return false;
            page.previewLive.PerformClick();if(page.previewDisconnected)return false;
            page.SwitchTarget(true);page.widgetScale.Value=200;page.widgetRadius.Value=18;
            page.keepInGames.Checked=true;page.widgetClickThrough.Checked=true;page.widgetEnabled.Checked=true;
            page.badgeColor=Color.Navy;page.inkColor=Color.Cyan;
            page.background.Checked=false;page.disconnectedHide.Checked=true;
            page.SwitchTarget(false);page.scale.Value=125;page.corner.Value=4;
            var saved=page.Read();
            if(saved.Scale!=125||saved.CornerRadius!=4||saved.Widget.Scale!=200||saved.Widget.CornerRadius!=18||saved.Widget.HideInGames||
                saved.Background==Color.Navy||saved.Widget.Background!=Color.Navy||saved.Widget.Text!=Color.Cyan||
                !saved.Widget.ClickThrough||!saved.ShowDesktopWidget||saved.Widget.ShowBackground||!saved.ShowDisconnectedIcon||saved.Widget.ShowDisconnectedIcon)return false;
            saved.Widget.Scale=250;
            if(page.Read().Widget.Scale!=200)return false;
            page.Set(page.Read());page.SwitchTarget(true);
            if(page.widgetScale.Value!=200||!page.widgetClickThrough.Checked||page.hideInGames.Checked)return false;
            page.Set(new OverlayAppearance());
            return !page.Read().ShowDesktopWidget&&page.Read().Widget.HideInGames&&!page.Read().Widget.ClickThrough;
        }
    }
}
