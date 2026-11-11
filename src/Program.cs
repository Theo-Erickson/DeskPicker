// DeskPicker
// Runs several GitHub Desktop copies side by side, each with its own data folder (and
// therefore its own signed-in GitHub account), via Electron's --user-data-dir switch.
// Sign-in always happens in Desktop's own browser flow: DeskPicker never sees a password
// or token. Profiles live in profiles.json next to the app. Removing a profile here never
// deletes its data folder.
//
// Build with build.cmd (Windows) or build.sh (macOS, Linux). Needs the .NET 10 SDK.

using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;

namespace DeskPicker
{
    class App : Application
    {
        public override void Initialize() { AppTheme.Init(this); }
    }

    static class Program
    {
        public const string Title = "DeskPicker";

        public static string LogPath { get { return Path.Combine(AppContext.BaseDirectory, "deskpicker-error.log"); } }

        [DllImport("user32.dll")] static extern IntPtr FindWindow(string cls, string title);
        [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr hwnd, int cmd);
        [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr hwnd);

        [STAThread]
        static int Main(string[] args)
        {
            // --open <name>: start that profile and quit (what the desktop shortcuts run).
            int open = Array.IndexOf(args, "--open");
            if (open >= 0 && open + 1 < args.Length) return OpenByName(args[open + 1]);

            // For the platform tests in ci/, with no window:
            // --ci-running: prints the data folders whose Desktop is running.
            // --ci-relay <data folder>: waits up to a minute for a sign-in link and hands it
            // to the Desktop using that folder, the way the window does for a waiting profile.
            if (args.Contains("--ci-running"))
            {
                foreach (var d in Desktop.RunningDirs()) Console.WriteLine(d);
                return 0;
            }
            int relay = Array.IndexOf(args, "--ci-relay");
            if (relay >= 0 && relay + 1 < args.Length) return RelayOnce(args[relay + 1]);

            bool first;
            using (var mutex = new Mutex(true, "DeskPicker.SingleInstance", out first))
            {
                if (!first)
                {
                    // Already open: bring that window forward instead of opening a second one.
                    if (OperatingSystem.IsWindows())
                    {
                        IntPtr h = FindWindow(null, Title);
                        if (h != IntPtr.Zero) { ShowWindow(h, 9); SetForegroundWindow(h); }
                    }
                    return 0;
                }

                AppBuilder.Configure<App>()
                    .UsePlatformDetect()
                    .With(new Win32PlatformOptions { OverlayPopups = true })
                    .SetupWithoutStarting();
                if (OperatingSystem.IsMacOS()) SignIn.ListenForLinks();
                Dispatcher.UIThread.UnhandledException += (s, e) =>
                {
                    Report(e.Exception);
                    e.Handled = true;
                };
                try
                {
                    var w = new MainWindow();
                    var done = new CancellationTokenSource();
                    w.Closed += delegate { done.Cancel(); };
                    w.Show();
                    Dispatcher.UIThread.MainLoop(done.Token);
                }
                catch (Exception e)
                {
                    Report(e);
                }
            }
            return 0;
        }

        static int OpenByName(string name)
        {
            try
            {
                var p = Profiles.Load().FirstOrDefault(x => string.Equals(x.name, name, StringComparison.OrdinalIgnoreCase));
                if (p == null) { Log("No profile called \"" + name + "\"."); return 1; }
                // A window that's still closing would make this one start signed out.
                if (!Profiles.IsRunning(Desktop.RunningDirs(), p) && !Profiles.WaitForStorage(p, 15)) { Log(p.name + " was still closing; not opened."); return 1; }
                Desktop.Launch(p, null, GitIdentity.Prepare(p));
                KeepTitle(p);
                return 0;
            }
            catch (Exception e) { Log(e.ToString()); return 1; }
        }

        static int RelayOnce(string dataDir)
        {
            if (!SignIn.CanRelay) { Console.WriteLine("can't relay on this system"); return 2; }
            var done = new ManualResetEventSlim();
            SignIn.Relayed += delegate { done.Set(); };
            SignIn.Watch(new SignIn.Pending { Profile = new Profile { name = "ci", dataDir = dataDir }, Since = DateTime.UtcNow });
            Console.WriteLine("watching");
            bool ok;
            if (OperatingSystem.IsMacOS())
            {
                // The link arrives as an Apple Event, which needs the app's event loop.
                AppBuilder.Configure<App>().UsePlatformDetect().SetupWithoutStarting();
                SignIn.ListenForLinks();
                var stop = new CancellationTokenSource(TimeSpan.FromMinutes(1));
                SignIn.Relayed += delegate { stop.Cancel(); };
                Dispatcher.UIThread.MainLoop(stop.Token);
                ok = done.IsSet;
            }
            else ok = done.Wait(TimeSpan.FromMinutes(1));
            SignIn.Watch(null);
            Console.WriteLine(ok ? "relayed" : "no link seen");
            return ok ? 0 : 1;
        }

        // Renames the profile's window once Desktop shows it, and keeps at it until Desktop
        // has gone a few seconds without putting its own title back (it can while it loads).
        static void KeepTitle(Profile p)
        {
            if (!OperatingSystem.IsWindows()) return;
            var start = DateTime.Now;
            DateTime? settled = null;
            while (DateTime.Now - start < TimeSpan.FromSeconds(60))
            {
                Thread.Sleep(300);
                var r = Desktop.Retitle(new[] { p });
                if (r.Item1 == 0 || r.Item2 > 0) settled = null;
                else if (settled == null) settled = DateTime.Now;
                else if (DateTime.Now - settled > TimeSpan.FromSeconds(8)) return;
            }
        }

        static void Log(string text)
        {
            try { File.AppendAllText(LogPath, DateTime.Now + "\n" + text + "\n\n"); } catch { }
        }

        // Anything unexpected: write it to the log and say where, so it can be fixed.
        public static void Report(Exception e)
        {
            Log(e.ToString());
            try { Dialogs.Info(null, "Something went wrong", e.Message + "\n\nDetails were saved to:\n" + LogPath); } catch { }
        }
    }
}
