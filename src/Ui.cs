using System;
using System.Collections.Generic;
using Avalonia.Controls.Shapes;

namespace DeskPicker
{
    // Small builders so the window reads like layout, not plumbing. Colors are always
    // theme keys (see Theme.cs), attached as dynamic references.
    static class Ui
    {
        // Atkinson Hyperlegible (SIL Open Font License), bundled from assets/fonts.
        public static readonly FontFamily UiFont = new FontFamily("avares://DeskPicker/Assets/Fonts#Atkinson Hyperlegible Next");
        public static readonly FontFamily Mono = new FontFamily("avares://DeskPicker/Assets/Fonts#Atkinson Hyperlegible Mono");

        // A property that follows a theme brush (like WPF's SetResourceReference).
        public static void SetResourceReference(this Control el, AvaloniaProperty p, string key)
        {
            el.Bind(p, el.GetResourceObservable(key));
        }

        public static ControlTheme Theme(string key) { return (ControlTheme)Application.Current.FindResource(key); }

        public static void Tip(this Control el, object tip) { ToolTip.SetTip(el, tip); }

        public static BoxShadows Shadow(double blur = 18, double y = 4, byte alpha = 90)
        {
            return new BoxShadows(new BoxShadow { OffsetX = 0, OffsetY = y, Blur = blur, Color = Color.FromArgb(alpha, 0, 0, 0) });
        }

        // Shows 'card' in a popup next to 'anchor'. Returns what closes it.
        public static Action ShowPopup(Control anchor, Control card, PlacementMode placement = PlacementMode.BottomEdgeAlignedRight)
        {
            var f = new Flyout { Content = card, Placement = placement };
            f.FlyoutPresenterClasses.Add("bare");
            f.ShowAt(anchor);
            return f.Hide;
        }

        public static TextBlock Text(string text, double size = 14, string brush = "Text", FontWeight? weight = null)
        {
            var t = new TextBlock { Text = text, FontSize = size, TextWrapping = TextWrapping.Wrap };
            if (weight.HasValue) t.FontWeight = weight.Value;
            t.SetResourceReference(TextBlock.ForegroundProperty, brush);
            return t;
        }

        // One line that ends in "..." when it doesn't fit.
        public static TextBlock Line(string text, double size = 14, string brush = "Text", FontWeight? weight = null)
        {
            var t = Text(text, size, brush, weight);
            t.TextWrapping = TextWrapping.NoWrap;
            t.TextTrimming = TextTrimming.CharacterEllipsis;
            return t;
        }

        public static Button Btn(string text, Action onClick, string style = "Btn")
        {
            var b = new Button { Content = text, Theme = Theme(style) };
            if (onClick != null) b.Click += delegate { onClick(); };
            return b;
        }

        public static Button IconBtn(string icon, string text, Action onClick, string style = "Btn", string iconBrush = null)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            var ic = Icon(icon, 15, iconBrush ?? (style == "BtnPrimary" ? "AccentText" : "Muted"), 2);
            sp.Children.Add(ic);
            if (text != null) sp.Children.Add(new TextBlock { Text = text, Margin = new Thickness(7, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
            var b = new Button { Content = sp, Theme = Theme(style) };
            if (onClick != null) b.Click += delegate { onClick(); };
            return b;
        }

        public static Border Panel(Control child, double padding = 16, string bg = "Panel", string border = "Border", double radius = 12)
        {
            var b = new Border { Child = child, Padding = new Thickness(padding), CornerRadius = new CornerRadius(radius), BorderThickness = new Thickness(1) };
            b.SetResourceReference(Border.BackgroundProperty, bg);
            b.SetResourceReference(Border.BorderBrushProperty, border);
            return b;
        }

        public static StackPanel Row(double gap, params Control[] items)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal, Spacing = gap };
            foreach (var it in items) sp.Children.Add(it);
            return sp;
        }

        // An icon and a line of text that gets the rest of the width, so it can end in "..."
        // (a horizontal StackPanel would give it unlimited room and let it spill out).
        public static Grid IconLine(string icon, TextBlock line, double gap = 8)
        {
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
            var ic = Icon(icon, 15);
            ic.Margin = new Thickness(0, 0, gap, 0);
            Grid.SetColumn(line, 1);
            line.VerticalAlignment = VerticalAlignment.Center;
            g.Children.Add(ic);
            g.Children.Add(line);
            line.Tip(line.Text);
            return g;
        }

        public static StackPanel Stack(double gap, params Control[] items)
        {
            var sp = new StackPanel { Spacing = gap };
            foreach (var it in items) sp.Children.Add(it);
            return sp;
        }

        // A small rounded label, like "Running" or "Default".
        public static Border Pill(string text, string bg, string fg, string dot = null)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            if (dot != null)
            {
                var e = new Ellipse { Width = 7, Height = 7, VerticalAlignment = VerticalAlignment.Center };
                e.SetResourceReference(Shape.FillProperty, dot);
                sp.Children.Add(e);
            }
            var t = Text(text, 11.5, fg, FontWeights.SemiBold);
            t.VerticalAlignment = VerticalAlignment.Center;
            sp.Children.Add(t);
            var b = new Border { Child = sp, CornerRadius = new CornerRadius(10), Padding = new Thickness(8, 2, 9, 3), VerticalAlignment = VerticalAlignment.Center };
            b.SetResourceReference(Border.BackgroundProperty, bg);
            return b;
        }

        // A notice strip: an icon, a message, and optional actions on the right.
        public static Border Banner(string kind, Control message, params Control[] actions)
        {
            var dock = new DockPanel();
            var ic = Icon(kind == "Info" ? "key" : "alert", 18, kind == "Err" ? "DotErr" : kind == "Warn" ? "DotWarn" : "DotInfo", 2);
            ic.Margin = new Thickness(0, 0, 12, 0);
            DockPanel.SetDock(ic, Dock.Left);
            dock.Children.Add(ic);
            if (actions.Length > 0)
            {
                var row = new WrapPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0), ItemSpacing = 8, LineSpacing = 6 };
                foreach (var a in actions) row.Children.Add(a);
                DockPanel.SetDock(row, Dock.Right);
                dock.Children.Add(row);
            }
            message.VerticalAlignment = VerticalAlignment.Center;
            dock.Children.Add(message);
            return Panel(dock, 14, kind + "Bg", kind + "Border", 10);
        }

        // ---------- line icons (24x24, stroked in a theme color) ----------

        static readonly Dictionary<string, string> IconPaths = new Dictionary<string, string>
        {
            { "plus", "M12 5v14 M5 12h14" },
            { "more", "M5 12h.01 M12 12h.01 M19 12h.01" },
            { "folder", "M3 6.5A1.5 1.5 0 0 1 4.5 5H9l2 2h8.5A1.5 1.5 0 0 1 21 8.5v9a1.5 1.5 0 0 1-1.5 1.5h-15A1.5 1.5 0 0 1 3 17.5z" },
            { "edit", "M4 20h4L19 9l-4-4L4 16z M13.5 6.5l4 4" },
            { "shortcut", "M14 4h6v6 M20 4l-9 9 M18 14v5a1 1 0 0 1-1 1H5a1 1 0 0 1-1-1V7a1 1 0 0 1 1-1h5" },
            { "trash", "M4 7h16 M9 7V4.5h6V7 M6.5 7l1 13h9l1-13" },
            { "alert", "M12 4 2.8 20h18.4z M12 10v4 M12 17v.01" },
            { "key", "M14.5 9.5a4 4 0 1 0-1.7 3.3L19 19h2v-2h-2v-2h-2l-2.3-2.3 M8.5 9.5h.01" },
            { "user", "M12 12a4 4 0 1 0 0-8 4 4 0 0 0 0 8z M4.5 20a7.5 7.5 0 0 1 15 0" },
            { "commit", "M12 15a3 3 0 1 0 0-6 3 3 0 0 0 0 6z M3 12h6 M15 12h6" },
            { "move", "M5 12h12 M13 7l5 5-5 5 M4 5v14" },
        };

        public static Control Icon(string name, double size = 18, string brush = "Muted", double stroke = 1.8)
        {
            var p = new Path
            {
                Data = Geometry.Parse(IconPaths[name]), StrokeThickness = name == "more" ? 3 : stroke,
                StrokeLineCap = PenLineCap.Round, StrokeJoin = PenLineJoin.Round
            };
            p.SetResourceReference(Shape.StrokeProperty, brush);
            var c = new Canvas { Width = 24, Height = 24 };
            c.Children.Add(p);
            return new Viewbox { Width = size, Height = size, Child = c, VerticalAlignment = VerticalAlignment.Center };
        }
    }
}
