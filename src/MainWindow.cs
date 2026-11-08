using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls.Shapes;

namespace DeskPicker
{
    // A headline, a card per profile (its account, its commit identity, open or not), and
    // notices at the bottom: Desktop missing, a sign-in being passed along, a profile kept
    // somewhere that gets cleaned out.
    class MainWindow : Window
    {
        List<Profile> profiles;
        HashSet<string> running = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        HashSet<string> signedIn;            // logins with a saved Desktop sign-in; null = can't tell
        SignIn.Pending waiting;              // a profile waiting for its browser sign-in
        Profile relayedTo;                   // ...and the last one its link was passed to
        DateTime relayedAt;
        bool refreshing, statusKnown;
        string shown;                        // what the page last showed, to skip needless redraws

        readonly TextBlock caption;
        readonly WrapPanel field;
        readonly StackPanel bottom;
        readonly DispatcherTimer timer;

        const double Gap = 16, CardMin = 280, Gutter = 14;

        public MainWindow()
        {
            Title = Program.Title;
            Width = 1000; Height = 720; MinWidth = 600; MinHeight = 480;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            FontFamily = Ui.UiFont;
            FontSize = 14;
            this.SetResourceReference(BackgroundProperty, "Bg");
            this.SetResourceReference(ForegroundProperty, "Text");
            UseLayoutRounding = true;
            try { Icon = new WindowIcon(Avalonia.Platform.AssetLoader.Open(new Uri("avares://DeskPicker/deskpicker.ico"))); } catch { }

            profiles = Profiles.Load();

            var root = new DockPanel { Margin = new Thickness(48, 32, 48, 24) };

            // headline
            // (adding a profile is the last card in the grid)
            var head = new DockPanel { Margin = new Thickness(0, 0, 0, 24) };
            caption = Ui.Text("", 12.5, "Muted");
            caption.LetterSpacing = 2;
            var title = new TextBlock { FontSize = 38, FontWeight = FontWeights.Light, LetterSpacing = -0.6 };
            title.Inlines.Add(new Avalonia.Controls.Documents.Run("Which GitHub "));
            var today = new Avalonia.Controls.Documents.Run("today?") { FontWeight = FontWeights.SemiBold };
            today.Bind(Avalonia.Controls.Documents.TextElement.ForegroundProperty, this.GetResourceObservable("Accent"));
            title.Inlines.Add(today);
            head.Children.Add(Ui.Stack(6, caption, title));
            DockPanel.SetDock(head, Dock.Top);
            root.Children.Add(head);

            // Notices scroll on their own past a share of the window, so a pile of them can't
            // push the profiles out of sight.
            bottom = new StackPanel { Spacing = 10, Margin = new Thickness(0, 0, Gutter, 0) };
            var notices = new ScrollViewer { Content = bottom, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Margin = new Thickness(0, 12, 0, 0) };
            SizeChanged += (s, e) => notices.MaxHeight = Math.Max(110, e.NewSize.Height * 0.36);
            DockPanel.SetDock(notices, Dock.Bottom);
            root.Children.Add(notices);

            // Cards share the width evenly: as many columns as fit at CardMin, each stretched
            // to fill the row, and every card in a row as tall as the tallest. The right
            // margin keeps the scrollbar off the cards; the top leaves room for a lifted folder.
            field = new WrapPanel { ItemSpacing = Gap, LineSpacing = Gap + 4, Margin = new Thickness(0, 4, Gutter, 10) };
            var scroll = new ScrollViewer { Content = field, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
            scroll.SizeChanged += (s, e) =>
            {
                double w = e.NewSize.Width - Gutter;
                int cols = Math.Max(1, (int)((w + Gap) / (CardMin + Gap)));
                field.ItemWidth = Math.Floor((w - Gap * (cols - 1)) / cols);
                FadeEdges(scroll);
            };
            scroll.ScrollChanged += delegate { FadeEdges(scroll); };
            root.Children.Add(scroll);
            Content = root;

            SignIn.Relayed += p => Dispatcher.UIThread.Post(delegate
            {
                relayedTo = p;
                relayedAt = DateTime.Now;
                Render(true);
            });

            Opened += delegate { RefreshStatus(); timer.Start(); };
            Render(true);

            timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            timer.Tick += delegate { RefreshStatus(); };
            Closed += delegate { timer.Stop(); SignIn.Watch(null); };
        }

        // ---------- page ----------

        // Cards fade out at an edge with more of them beyond it, instead of being cut off.
        static void FadeEdges(ScrollViewer s)
        {
            double h = s.Bounds.Height;
            bool above = s.Offset.Y > 0.5, below = s.Offset.Y + s.Viewport.Height < s.Extent.Height - 0.5;
            if (h <= 0 || (!above && !below)) { s.OpacityMask = null; return; }
            double f = Math.Min(0.25, 28 / h);
            var b = new LinearGradientBrush { StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative) };
            b.GradientStops.Add(new GradientStop(above ? Colors.Transparent : Colors.Black, 0));
            b.GradientStops.Add(new GradientStop(Colors.Black, f));
            b.GradientStops.Add(new GradientStop(Colors.Black, 1 - f));
            b.GradientStops.Add(new GradientStop(below ? Colors.Transparent : Colors.Black, 1));
            s.OpacityMask = b;
        }

        void Render(bool force = false)
        {
            bool recentlyRelayed = relayedTo != null && DateTime.Now - relayedAt < TimeSpan.FromSeconds(20);
            string state = string.Join("|", profiles.Select(p => p.name + ":" + Profiles.IsRunning(running, p) + ":" + p.dataDir + ":" + p.githubLogin + ":" + p.gitName + ":" + p.gitEmail))
                + "#" + (signedIn == null ? "?" : string.Join(",", signedIn.OrderBy(x => x)))
                + "#" + (waiting == null ? "" : waiting.Profile.name + waiting.Since.Ticks)
                + "#" + (recentlyRelayed ? relayedTo.name : "") + "#" + statusKnown;
            if (!force && state == shown) return;
            shown = state;

            int on = profiles.Count(p => Profiles.IsRunning(running, p));
            caption.Text = (CultureInfo.CurrentCulture.DateTimeFormat.GetDayName(DateTime.Now.DayOfWeek) + " · " + on + " of " + profiles.Count + " open").ToUpperInvariant();

            field.Children.Clear();
            if (statusKnown)
            {
                foreach (var p in profiles) field.Children.Add(Card(p));
                field.Children.Add(NewCard());
            }

            bottom.Children.Clear();
            if (!Desktop.Found)
            {
                bottom.Children.Add(Ui.Banner("Err", Ui.Stack(2,
                    Ui.Text("GitHub Desktop isn't installed", 14, "ErrText", FontWeights.SemiBold),
                    Ui.Text("DeskPicker looks for it at " + Desktop.WhereWeLook + ". Install it from desktop.github.com, then reopen DeskPicker.", 12.5, "ErrText"))));
            }
            foreach (var p in profiles.Where(Profiles.InTemp))
            {
                var prof = p;
                bottom.Children.Add(Ui.Banner("Warn", Ui.Stack(2,
                    Ui.Text(p.name + " is kept in the temp folder", 14, "WarnText", FontWeights.SemiBold),
                    Ui.Text("Cleanup tools can empty it, which would sign this profile out and forget its repositories. Close " + p.name + ", then move it.", 12.5, "WarnText")),
                    Ui.IconBtn("move", "Move to a safe folder", delegate { MoveToSafety(prof); }, "Btn", "Text")));
            }
            if (waiting != null)
            {
                string n = waiting.Profile.name;
                bottom.Children.Add(SignIn.CanRelay
                    ? Ui.Banner("Info", Ui.Stack(2,
                        Ui.Text(n + " is signing in", 14, "InfoText", FontWeights.SemiBold),
                        Ui.Text("Finish in your browser. The browser hands the sign-in to your main GitHub Desktop, so DeskPicker passes it on to " + n + ". Keep DeskPicker open until it's done.", 12.5, "Sub")))
                    : Ui.Banner("Warn", Ui.Stack(2,
                        Ui.Text(n + " is signing in", 14, "WarnText", FontWeights.SemiBold),
                        Ui.Text("On this system the browser's sign-in goes to the first GitHub Desktop that's open. If it doesn't reach " + n + ", close the other GitHub Desktop windows and sign in again.", 12.5, "WarnText"))));
            }
            else if (recentlyRelayed)
            {
                bottom.Children.Add(Ui.Banner("Info", Ui.Text("Passed the sign-in on to " + relayedTo.name + ". Your main GitHub Desktop will say the sign-in wasn't for it; you can ignore that.", 13, "Sub")));
            }
        }

        Control Card(Profile p)
        {
            bool on = Profiles.IsRunning(running, p);

            // The profile's name is written on the folder's tab; its status sits beside it.
            var name = Ui.Line(p.name, 16, "Ink", FontWeights.SemiBold);
            name.Tip(p.name);
            var pill = on ? Ui.Pill("Running", "RunBg", "OkText", "DotOk") : Ui.Pill(Profiles.IsDefault(p) ? "Closed · default" : "Closed", "Panel", "Muted");

            string account, accountBrush = "InkSub";
            if (string.IsNullOrEmpty(p.githubLogin)) { account = "No GitHub account set"; accountBrush = "InkMuted"; }
            else if (signedIn == null) account = p.githubLogin;
            else if (signedIn.Contains(p.githubLogin)) account = p.githubLogin + " · signed in";
            else { account = p.githubLogin + " · not signed in yet"; accountBrush = "InkWarn"; }

            string commits = GitIdentity.Has(p)
                ? ((p.gitName ?? "(global name)") + " <" + (p.gitEmail ?? "global email") + ">")
                : "Your global git identity";

            // The end of the path is the part that tells profiles apart, so trim the start.
            var folder = Ui.Line(Profiles.IsDefault(p) ? "Desktop's default folder" : p.dataDir, 11.5, "InkMuted");
            folder.FontFamily = Ui.Mono;
            folder.TextTrimming = TextTrimming.PrefixCharacterEllipsis;
            folder.Tip(Profiles.DataDirOf(p));

            var openBtn = Ui.Btn(on ? "Show window" : "Open", delegate { Open(p); }, on ? "BtnPillInk" : "BtnPill");
            var more = Ui.IconBtn("more", null, null, "BtnRoundInk", "InkSub");
            more.Tip("More");
            Avalonia.Automation.AutomationProperties.SetName(more, "More for " + p.name);
            more.Click += delegate { ShowMenu(more, p); };
            var actions = Ui.Row(6, openBtn, more);
            actions.Margin = new Thickness(0, 6, 0, 0);

            var body = Ui.Stack(9,
                Ui.IconLine("user", Ui.Line(account, 13.5, accountBrush), 8, "InkMuted"),
                Ui.IconLine("commit", Ui.Line(commits, 13.5, "InkSub"), 8, "InkMuted"),
                folder, actions);
            var card = Ui.Folder(name, body, pill, on);
            card.DoubleTapped += delegate { Open(p); };
            return card;
        }

        Control NewCard()
        {
            var tab = Ui.Row(7, Ui.Icon("plus", 14, "Muted", 2), Ui.Text("New profile", 14, "Sub", FontWeights.Medium));
            var sub = Ui.Text("Its own sign-in and repositories", 12.5, "Muted");
            sub.HorizontalAlignment = HorizontalAlignment.Center;
            sub.VerticalAlignment = VerticalAlignment.Center;
            sub.TextAlignment = TextAlignment.Center;
            var b = new Button { Theme = Ui.Theme("Bare"), Content = Ui.Folder(tab, sub, null, false, true), MinHeight = 190 };
            Avalonia.Automation.AutomationProperties.SetName(b, "Add a new profile");
            b.Click += delegate { AddProfile(); };
            return b;
        }

        void ShowMenu(Control anchor, Profile p)
        {
            Action close = null;
            var list = new StackPanel { MinWidth = 210 };
            Action<string, string, Action, string> item = (icon, text, act, brush) =>
            {
                var r = Ui.Row(10, Ui.Icon(icon, 16, brush ?? "Muted"), Ui.Text(text, 13, brush ?? "Text"));
                var b = new Button { Theme = Ui.Theme("Row"), Content = r };
                b.Click += delegate { close(); act(); };
                list.Children.Add(b);
            };
            item("edit", "Edit...", delegate { EditProfile(p); }, null);
            item("folder", "Open data folder", delegate { OpenFolder(p); }, null);
            item("shortcut", "Make a shortcut", delegate { CreateShortcut(p); }, null);
            if (!Profiles.IsDefault(p))
            {
                var sep = new Border { Height = 1, Margin = new Thickness(6, 4, 6, 4) };
                sep.SetResourceReference(Border.BackgroundProperty, "Border");
                list.Children.Add(sep);
                item("trash", "Remove from list", delegate { RemoveProfile(p); }, "DotErr");
            }
            var card = Ui.Panel(list, 6, "Panel", "Line");
            card.Margin = new Thickness(12, 4, 12, 12);
            card.BoxShadow = Ui.Shadow();
            close = Ui.ShowPopup(anchor, card);
        }

        // ---------- status ----------

        void RefreshStatus()
        {
            if (refreshing) return;
            refreshing = true;
            var snapshot = profiles.ToList();
            Task.Run(() =>
            {
                var r = Desktop.RunningDirs();
                var s = Keychain.SignedInLogins();
                var w = SignIn.Waiting(snapshot, r);
                return Tuple.Create(r, s, w);
            }).ContinueWith(t => Dispatcher.UIThread.Post(delegate
            {
                refreshing = false;
                if (t.IsFaulted) return;
                running = t.Result.Item1;
                signedIn = t.Result.Item2;
                waiting = t.Result.Item3;
                statusKnown = true;
                SignIn.Watch(waiting);
                Render();
            }));
        }

        void RefreshSoon()
        {
            DispatcherTimer.RunOnce(RefreshStatus, TimeSpan.FromSeconds(1.5));
        }

        // ---------- actions ----------

        bool Ready()
        {
            if (Desktop.Found) return true;
            Dialogs.Info(this, "GitHub Desktop isn't installed", "DeskPicker looks for it at\n" + Desktop.WhereWeLook);
            return false;
        }

        void Open(Profile p)
        {
            if (!Ready()) return;
            // Launching a profile that's already running just brings its window forward.
            Desktop.Launch(p, null, GitIdentity.Prepare(p));
            RefreshSoon();
        }

        void Save()
        {
            Profiles.Save(profiles);
            Render(true);
        }

        void AddProfile()
        {
            var p = new Profile { dataDir = "" };
            if (!Dialogs.EditProfile(this, p, true, profiles.Count)) return;
            profiles.Add(p);
            Save();
        }

        void EditProfile(Profile p)
        {
            var copy = new Profile { name = p.name, dataDir = p.dataDir, githubLogin = p.githubLogin, gitName = p.gitName, gitEmail = p.gitEmail };
            if (!Dialogs.EditProfile(this, copy, false, profiles.IndexOf(p))) return;
            bool identityChanged = copy.gitName != p.gitName || copy.gitEmail != p.gitEmail || copy.githubLogin != p.githubLogin;
            p.name = copy.name; p.githubLogin = copy.githubLogin; p.gitName = copy.gitName; p.gitEmail = copy.gitEmail;
            Save();
            if (identityChanged && Profiles.IsRunning(running, p))
                Dialogs.Info(this, "Saved", "Close and reopen " + p.name + " for the new commit identity to take effect.");
        }

        void RemoveProfile(Profile p)
        {
            if (!Dialogs.Ask(this, "Remove " + p.name + "?", "It's only taken off this list. Its data folder is kept:\n" + p.dataDir, "Remove", "Cancel", false)) return;
            profiles.Remove(p);
            Save();
        }

        void MoveToSafety(Profile p)
        {
            if (Profiles.IsRunning(running, p))
            {
                Dialogs.Info(this, "Close " + p.name + " first", "Its GitHub Desktop window is still open, and its files are in use. Close it, then move it.");
                return;
            }
            string to = Profiles.NewDataDir(p.name, profiles.IndexOf(p));
            for (int i = 2; Directory.Exists(to); i++) to = Profiles.NewDataDir(p.name, profiles.IndexOf(p)) + "-" + i;
            if (!Dialogs.Ask(this, "Move " + p.name + "?", "From\n" + p.dataDir + "\n\nto\n" + to + "\n\nIt stays signed in: the sign-in itself is saved in your system's keychain, not in this folder.", "Move it")) return;
            try { Profiles.MoveData(p, to); }
            catch (Exception e)
            {
                Dialogs.Info(this, "Couldn't move it", e.Message + "\n\nNothing was changed in the profile list.");
                return;
            }
            Save();
        }

        void OpenFolder(Profile p)
        {
            string dir = Profiles.DataDirOf(p);
            if (Directory.Exists(dir)) Profiles.OpenFolder(dir);
            else Dialogs.Info(this, "No folder yet", "Open " + p.name + " once first and GitHub Desktop will create it.");
        }

        void CreateShortcut(Profile p)
        {
            string where = Profiles.CreateShortcut(p);
            if (where == null) Dialogs.Info(this, "Not on this system yet", "Shortcuts aren't supported here yet. Run DeskPicker --open \"" + p.name + "\" instead.");
            else Dialogs.Info(this, "Shortcut created", "It's in " + where + ". It opens " + p.name + " with its commit identity.");
        }
    }
}
