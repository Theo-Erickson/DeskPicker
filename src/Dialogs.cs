using System;

namespace DeskPicker
{
    // A dialog window that follows the theme (background, fonts, title bar).
    class ThemedWindow : Window
    {
        readonly Window owner;

        public ThemedWindow(Window owner, string title, double width)
        {
            this.owner = owner;
            Title = title;
            Width = width;
            SizeToContent = SizeToContent.Height;
            CanResize = false;
            ShowInTaskbar = false;
            WindowStartupLocation = owner != null ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen;
            FontFamily = Ui.UiFont;
            FontSize = 14;
            this.SetResourceReference(BackgroundProperty, "Bg");
            this.SetResourceReference(ForegroundProperty, "Text");
            KeyDown += (s, e) => { if (e.Key == Key.Escape) Close(); };
        }

        // Shows the dialog and returns once it's closed, so callers can read what was
        // picked on the next line.
        public void ShowModal()
        {
            var frame = new DispatcherFrame();
            Closed += delegate { frame.Continue = false; };
            if (owner != null && owner.IsVisible) ShowDialog(owner); else Show();
            Dispatcher.UIThread.PushFrame(frame);
        }

        // Standard layout: content, then a right-aligned button row.
        public void Layout(Control content, params Button[] buttons)
        {
            var root = new DockPanel { Margin = new Thickness(24, 20, 24, 20) };
            var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 22, 0, 0), Spacing = 8 };
            foreach (var b in buttons) { b.MinWidth = 90; row.Children.Add(b); }
            DockPanel.SetDock(row, Dock.Bottom);
            root.Children.Add(row);
            root.Children.Add(content);
            Content = root;
        }
    }

    static class Dialogs
    {
        public static bool Ask(Window owner, string title, string message, string ok = "OK", string cancel = "Cancel", bool primary = true)
        {
            var w = new ThemedWindow(owner, title, 480);
            bool result = false;
            var okBtn = Ui.Btn(ok, delegate { result = true; w.Close(); }, primary ? "BtnPrimary" : "Btn");
            okBtn.IsDefault = true;
            var content = Ui.Stack(10, Ui.Text(title, 18, "Text", FontWeights.SemiBold), Ui.Text(message, 14, "Sub"));
            var buttons = cancel != null
                ? new[] { Ui.Btn(cancel, delegate { w.Close(); }), okBtn }
                : new[] { okBtn };
            w.Layout(content, buttons);
            w.ShowModal();
            return result;
        }

        public static void Info(Window owner, string title, string message)
        {
            Ask(owner, title, message, "OK", null, true);
        }

        // Add a profile, or edit one: its name, the GitHub account it's for, and who its
        // commits are by. Returns false if cancelled.
        public static bool EditProfile(Window owner, Profile p, bool adding, int index)
        {
            var w = new ThemedWindow(owner, adding ? "New profile" : "Edit " + p.name, 520);
            bool saved = false;

            var name = new TextBox { Text = adding ? "" : p.name, PlaceholderText = "e.g. Work or Personal" };
            var login = new TextBox { Text = p.githubLogin ?? "", PlaceholderText = "e.g. octocat" };
            var gitName = new TextBox { Text = p.gitName ?? "", PlaceholderText = "Leave empty to use your global git name" };
            var gitEmail = new TextBox { Text = p.gitEmail ?? "", PlaceholderText = "Leave empty to use your global git email" };
            var title = new TextBox { Text = p.windowTitle ?? "" };
            Action titleHint = delegate { title.PlaceholderText = Profiles.DefaultWindowTitle(string.IsNullOrWhiteSpace(name.Text) ? "Name" : name.Text); };
            titleHint();
            name.TextChanged += delegate { titleHint(); };
            Func<string> dir = delegate { return adding ? Profiles.NewDataDir((name.Text ?? "").Trim(), index) : Profiles.DataDirOf(p); };
            var where = Ui.Line(dir(), 11.5, "Faint");
            where.FontFamily = Ui.Mono;
            name.TextChanged += delegate { where.Text = dir(); };

            Func<string, string, Control, Control> field = (label, hint, box) =>
            {
                var sp = Ui.Stack(5, Ui.Text(label, 12, "Muted"), box);
                if (hint != null) sp.Children.Add(Ui.Text(hint, 11.5, "Faint"));
                return sp;
            };

            var save = Ui.Btn(adding ? "Add profile" : "Save", delegate
            {
                string n = (name.Text ?? "").Trim();
                if (n.Length == 0) { name.Focus(); return; }
                p.name = n;
                if (adding) p.dataDir = dir();
                p.githubLogin = Clean(login.Text);
                p.gitName = Clean(gitName.Text);
                p.gitEmail = Clean(gitEmail.Text);
                p.windowTitle = Clean(title.Text);
                saved = true;
                w.Close();
            }, "BtnPrimary");
            save.IsDefault = true;

            var content = Ui.Stack(14,
                Ui.Stack(6,
                    Ui.Text(adding ? "New profile" : "Edit " + p.name, 20, "Text", FontWeights.SemiBold),
                    Ui.Text(adding
                        ? "A profile is its own copy of GitHub Desktop, with its own sign-in and repository list. Sign in from its window after you open it."
                        : "Changes to the commit identity apply the next time this profile opens.", 13, "Muted")),
                Ui.Stack(5, Ui.Text("Name", 12, "Muted"), name, where),
                field("GitHub account", "The login this profile signs in with. Used to show whether it's signed in.", login),
                field("Commit name", null, gitName),
                field("Commit email", "Commits made in this profile use these. Your global git config is left alone.", gitEmail),
                field("Window title", "What this profile's GitHub Desktop window is called in the taskbar and Alt+Tab. Leave empty for the one shown.", title));
            w.Layout(content, Ui.Btn("Cancel", delegate { w.Close(); }), save);
            w.Opened += delegate { name.Focus(); name.SelectAll(); };
            w.ShowModal();
            return saved;
        }

        static string Clean(string s)
        {
            s = (s ?? "").Trim();
            return s.Length == 0 ? null : s;
        }
    }
}
