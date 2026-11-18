using System;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls.Shapes;
using Avalonia.Media.Transformation;

namespace DeskPicker
{
    // A profile's manila folder. The name is on the tab and the status beside it; the details
    // are written on the front flap, and the embossed GitHub mark in its corner is the Open
    // button. Three pages sit inside: they fan up out of the folder under the pointer. While
    // the profile's Desktop runs, the pages stand up out of it, the middle one (edged green) breathes,
    // and git branches grow out of the folder's bottom with commits flowing down them.
    class FolderCard : Grid
    {
        static readonly Easing Spring = new BackEaseOut();
        static readonly TimeSpan PageTime = TimeSpan.FromSeconds(0.55);
        public const double Headroom = 4;
        const double PageRise = 12;   // how far above the folder's mouth the pages may show

        readonly Border pageA, pageB, pageC;
        readonly Panel breath;
        readonly Roots roots;
        bool on, hover, looping;
        TimeSpan? last;

        public bool Running => on;

        // 'mark' is the folder's main button, at the flap's right; 'more' sits beside the status.
        public FolderCard(Control tab, Control status, Control details, Control more, Control mark, bool running)
        {
            RowDefinitions = new RowDefinitions("Auto,Auto,Auto");
            Classes.Add("folder");
            Margin = new Thickness(0, Headroom, 0, 0);   // room above the tab for the pages to rise into
            on = running;

            // ---- tab and status ----
            var tabB = new Border { Child = tab, Padding = new Thickness(18, 8, 18, 9), CornerRadius = new CornerRadius(11, 11, 0, 0), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, -1) };
            tabB.SetResourceReference(Border.BackgroundProperty, "ManilaBack");
            var head = new DockPanel();
            status.VerticalAlignment = VerticalAlignment.Center;
            var side = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, Margin = new Thickness(12, 0, -6, 2), VerticalAlignment = VerticalAlignment.Bottom };
            side.Children.Add(status);
            side.Children.Add(more);
            DockPanel.SetDock(side, Dock.Right);
            head.Children.Add(side);
            head.Children.Add(tabB);

            // ---- the pocket: back, pages, front flap ----
            var pocket = new Grid();
            Grid.SetRow(pocket, 1);
            var back = new Border { CornerRadius = new CornerRadius(0, 13, 13, 13) };
            back.SetResourceReference(Border.BackgroundProperty, "ManilaBack");
            pocket.Children.Add(back);

            // The pages live in a layer clipped to the folder (plus a sliver over the tab's foot),
            // so however they move they never show past its sides or below it.
            var pages = new Panel { ClipToBounds = true, Margin = new Thickness(0, -PageRise, 0, 0) };
            pageA = Page("Paper2", new Thickness(14, PageRise, 24, 0));
            pageB = Page("Paper", new Thickness(14, PageRise, 24, 0));
            pageC = Page("Paper3", new Thickness(26, PageRise, 12, 0));
            pageB.SetResourceReference(Border.BorderBrushProperty, "Accent");
            breath = new Panel { RenderTransform = new TranslateTransform() };
            breath.Children.Add(pageB);
            pages.Children.Add(pageA);
            pages.Children.Add(breath);
            pages.Children.Add(pageC);
            pocket.Children.Add(pages);

            // the details on the left, the embossed mark (the Open button) on the right
            var flap = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
            details.VerticalAlignment = VerticalAlignment.Center;
            flap.Children.Add(details);
            mark.VerticalAlignment = VerticalAlignment.Center;
            mark.Margin = new Thickness(8, -8, -8, -8);
            Grid.SetColumn(mark, 1);
            flap.Children.Add(mark);

            var front = new Border
            {
                Child = flap, Padding = new Thickness(18, 15, 18, 15), Margin = new Thickness(0, 22, 0, 0),
                CornerRadius = new CornerRadius(6, 6, 13, 13), BorderThickness = new Thickness(0, 1, 0, 0),
                BoxShadow = new BoxShadows(new BoxShadow { OffsetY = -2, Blur = 8, Color = Color.FromArgb(51, 60, 40, 10) }),
            };
            front.SetResourceReference(Border.BackgroundProperty, "Manila");
            front.SetResourceReference(Border.BorderBrushProperty, "ManilaEdge");
            pocket.Children.Add(front);

            roots = new Roots { Height = 56, Margin = new Thickness(0, -3, 0, 0), IsHitTestVisible = false };
            Grid.SetRow(roots, 2);

            // the pages stand in front of the tab, as in a real folder
            Children.Add(head);
            Children.Add(pocket);
            Children.Add(roots);

            roots.Grown = on ? Roots.FullyGrown : 0;
            Apply();
            PointerEntered += delegate { hover = true; Apply(); };
            PointerExited += delegate { hover = false; Apply(); };
            AttachedToVisualTree += delegate { Loop(); };
        }

        static Border Page(string brush, Thickness margin)
        {
            // a few ruled lines, so it reads as a sheet of paper
            var lines = new StackPanel { Spacing = 6, Margin = new Thickness(14, 14, 0, 0), Width = 120, HorizontalAlignment = HorizontalAlignment.Left };
            for (int i = 0; i < 3; i++)
            {
                var l = new Border { Height = 3, CornerRadius = new CornerRadius(2), Width = 120 - i * 18, HorizontalAlignment = HorizontalAlignment.Left };
                l.SetResourceReference(Border.BackgroundProperty, "PaperLine");
                lines.Children.Add(l);
            }
            var b = new Border
            {
                Child = lines, Height = 70, VerticalAlignment = VerticalAlignment.Top, Margin = margin, CornerRadius = new CornerRadius(4, 4, 0, 0),
                RenderTransformOrigin = new RelativePoint(0.5, 1, RelativeUnit.Relative),
                Transitions = new Transitions { new TransformOperationsTransition { Property = Visual.RenderTransformProperty, Duration = PageTime, Easing = Spring } },
            };
            b.SetResourceReference(Border.BackgroundProperty, brush);
            return b;
        }

        // Opens or closes the folder, with its animation (the cards are rebuilt when a
        // profile's state changes, so MainWindow builds the old state and then calls this).
        public void SetRunning(bool value)
        {
            if (on == value) return;
            on = value;
            Apply();
            Loop();
        }

        void Apply()
        {
            // Closed, the pages sit low in the folder; open, they stand up in its mouth; under
            // the pointer they lift and fan a little. They rise at most to the tab's foot.
            pageA.RenderTransform = TransformOperations.Parse(on ? (hover ? "translate(-5px,-7px) rotate(-2.5deg)" : "translate(0px,-2px) rotate(-1deg)") : (hover ? "translate(-5px,-3px) rotate(-2.5deg)" : "translate(0px,9px) rotate(0deg)"));
            pageB.RenderTransform = TransformOperations.Parse(on ? (hover ? "translate(0px,-8px) rotate(0deg)" : "translate(0px,-4px) rotate(0deg)") : (hover ? "translate(0px,-6px) rotate(0deg)" : "translate(0px,6px) rotate(0deg)"));
            pageC.RenderTransform = TransformOperations.Parse(on ? (hover ? "translate(5px,-6px) rotate(2.5deg)" : "translate(0px,0px) rotate(1deg)") : (hover ? "translate(5px,-2px) rotate(2.5deg)" : "translate(0px,11px) rotate(0deg)"));
            pageB.BorderThickness = new Thickness(0, on ? 3 : 0, 0, 0);
        }

        // One frame loop per folder, running only while something moves: the branches growing
        // or pulling back, and then (while open) the commits and the breathing page.
        void Loop()
        {
            if (looping) return;
            var top = TopLevel.GetTopLevel(this);
            if (top == null) return;
            looping = true;
            last = null;
            top.RequestAnimationFrame(Frame);
        }

        void Frame(TimeSpan now)
        {
            double dt = last.HasValue ? Math.Min(0.1, (now - last.Value).TotalSeconds) : 0;
            last = now;
            var top = TopLevel.GetTopLevel(this);
            if (top == null) { looping = false; return; }

            roots.Grown = on ? Math.Min(Roots.FullyGrown, roots.Grown + dt) : Math.Max(0, roots.Grown - dt * 2);
            roots.Clock = now.TotalSeconds;
            roots.InvalidateVisual();
            ((TranslateTransform)breath.RenderTransform).Y = on ? -1.2 * (1 - Math.Cos(now.TotalSeconds * 2 * Math.PI / 3.4)) : 0;

            if (on || roots.Grown > 0) top.RequestAnimationFrame(Frame);
            else looping = false;
        }
    }

    // Git branches under an open folder: they draw themselves down out of the folder one after
    // another, commit nodes pop in along them, and once grown, commits travel down each branch.
    // Drawn in a 320x88 box, scaled to fit and centered.
    class Roots : Control
    {
        public const double FullyGrown = 1.9;   // seconds of growing until everything is out

        // Like a git graph: a trunk, two branches off it, and one off each of those. Each
        // branch starts exactly on its parent's line and runs straight down after its curve.
        static readonly (string path, double delay)[] Branches =
        {
            ("M160 0 V84", 0),                                 // trunk
            ("M160 12 C160 26 104 20 104 36 V74", 0.1),        // left, from the trunk
            ("M160 30 C160 44 216 38 216 50 V78", 0.3),        // right, from the trunk
            ("M104 44 C104 56 60 52 60 64 V78", 0.55),         // from the left branch
            ("M216 56 C216 66 258 62 258 72", 0.65),           // from the right branch
        };
        // Commits sit where a branch starts (on its parent's line) and at each branch's tip,
        // popping in as the growing line reaches them.
        static readonly (double x, double y, double delay)[] Nodes =
        {
            (160, 12, 0.15), (160, 30, 0.35), (104, 44, 0.6), (216, 56, 0.75),
            (160, 84, 0.95), (104, 74, 1.05), (216, 78, 1.25), (60, 78, 1.5), (258, 72, 1.55),
        };
        static readonly Geometry[] Shapes = Array.ConvertAll(Branches, b => Geometry.Parse(b.path));
        static readonly Easing Ease = new CubicEaseInOut(), Pop = new BackEaseOut();

        public double Grown;   // 0 = nothing out, FullyGrown = all of it
        public double Clock;   // seconds, for the commits' travel

        IBrush Brush(string key) { return this.TryFindResource(key, out var o) ? o as IBrush : Brushes.Green; }

        public override void Render(DrawingContext ctx)
        {
            if (Grown <= 0) return;
            double s = Math.Min(Bounds.Width / 320, Bounds.Height / 88);
            if (s <= 0) return;
            var pen = new Pen(Brush("Accent"), 2.2, lineCap: PenLineCap.Round);
            var desk = Brush("Bg");
            using (ctx.PushTransform(Matrix.CreateScale(s, s) * Matrix.CreateTranslation((Bounds.Width - 320 * s) / 2, 0)))
            {
                for (int i = 0; i < Shapes.Length; i++)
                {
                    double p = Ease.Ease(Math.Clamp((Grown - Branches[i].delay) / 0.9, 0, 1));
                    if (p <= 0) continue;
                    var g = Shapes[i];
                    if (p >= 1) ctx.DrawGeometry(null, pen, g);
                    else if (g.TryGetSegment(0, p * g.ContourLength, true, out var seg)) ctx.DrawGeometry(null, pen, seg);
                }
                foreach (var n in Nodes)
                {
                    double k = Math.Clamp((Grown - n.delay) / 0.35, 0, 1);
                    if (k <= 0) continue;
                    double r = 4 * Pop.Ease(k);
                    ctx.DrawEllipse(desk, pen, new Point(n.x, n.y), r, r);
                }
                if (Grown < FullyGrown) return;
                var packet = Brush("OkText");
                for (int i = 0; i < Shapes.Length; i++)
                {
                    var g = Shapes[i];
                    double u = (Clock * (0.32 + (i % 3) * 0.07) + i * 0.37) % 1;
                    if (!g.TryGetPointAtDistance(u * g.ContourLength, out var pt)) continue;
                    double fade = u < 0.08 ? u / 0.08 : u > 0.9 ? (1 - u) / 0.1 : 1;
                    using (ctx.PushOpacity(fade))
                        ctx.DrawEllipse(packet, null, pt, i == 0 ? 3 : 2.6, i == 0 ? 3 : 2.6);
                }
            }
        }
    }
}
