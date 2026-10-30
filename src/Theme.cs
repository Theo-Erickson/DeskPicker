using System.Collections.Generic;

namespace DeskPicker
{
    // A dark palette with a green accent. Every color in the UI is a named brush in the
    // app's resources.
    static class AppTheme
    {
        static readonly Dictionary<string, string> Palette = new Dictionary<string, string>
        {
            {"Bg","#0d1117"}, {"Panel","#151b24"}, {"PanelHover","#1b2330"}, {"Input","#0d1117"}, {"RunBg","#142a1d"},
            {"Border","#232b37"}, {"Line","#2f3744"}, {"Dashed","#394252"}, {"Text","#e6ebf2"}, {"Sub","#c4ccd8"}, {"Muted","#8b95a5"}, {"Faint","#6b7585"},
            {"Link","#6cb2ff"}, {"Accent","#4fc06a"}, {"AccentText","#06150b"}, {"SelRow","#1b2330"},
            {"WarnBg","#2a2412"}, {"WarnBorder","#5a4a1a"}, {"WarnText","#f3e3b4"},
            {"ErrBg","#2c1618"}, {"ErrBorder","#62302f"}, {"ErrText","#f6c9c6"},
            {"InfoBg","#111c2e"}, {"InfoBorder","#26385a"}, {"InfoText","#e6ebf2"},
            {"OkText","#86e0a0"}, {"OkBorder","#2f6b43"},
            {"DotOk","#4fc06a"}, {"DotWarn","#e8b339"}, {"DotErr","#f2706b"}, {"DotInfo","#6cb2ff"},
            {"Scroll","#2f3744"},
        };

        // Fluent's own brushes for the controls DeskPicker doesn't restyle, pointed at ours.
        static readonly Dictionary<string, string> FluentKeys = new Dictionary<string, string>
        {
            {"TextControlForeground","Text"}, {"TextControlForegroundPointerOver","Text"}, {"TextControlForegroundFocused","Text"},
            {"TextControlBackground","Input"}, {"TextControlBackgroundPointerOver","Input"}, {"TextControlBackgroundFocused","Input"},
            {"TextControlBorderBrush","Line"}, {"TextControlBorderBrushPointerOver","Dashed"}, {"TextControlBorderBrushFocused","Accent"},
            {"TextControlPlaceholderForeground","Faint"},
            {"ToolTipBackground","Panel"}, {"ToolTipForeground","Text"}, {"ToolTipBorderBrush","Line"},
        };

        public static void Init(Application app)
        {
            app.Styles.Add(new Avalonia.Themes.Fluent.FluentTheme());
            app.Styles.Add(new DeskStyles());
            foreach (var kv in Palette) app.Resources[kv.Key] = new SolidColorBrush(Color.Parse(kv.Value));
            foreach (var kv in FluentKeys) app.Resources[kv.Key] = app.Resources[kv.Value];
            // Fluent's own parts and the window title bar follow this.
            app.RequestedThemeVariant = ThemeVariant.Dark;
        }
    }
}
