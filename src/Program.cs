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
                Desktop.Launch(p, null, GitIdentity.Prepare(p));
                return 0;
            }
            catch (Exception e) { Log(e.ToString()); return 1; }
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
