using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace DeskPicker
{
    // Everything that touches GitHub Desktop itself: where it's installed, its default data
    // folder, starting it for a profile and seeing which profiles are running. A profile is
    // just a data folder handed to Electron's --user-data-dir switch.
    static class Desktop
    {
        static readonly string Home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        static readonly string AppData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        static readonly string LocalAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        const string MacApp = "/Applications/GitHub Desktop.app";
        const string Flatpak = "io.github.shiftey.Desktop";

        // Where Desktop keeps its data when it's started normally.
        public static readonly string DefaultDir =
            OperatingSystem.IsMacOS() ? Path.Combine(Home, "Library", "Application Support", "GitHub Desktop")
            : Path.Combine(AppData, "GitHub Desktop");   // %APPDATA% on Windows, ~/.config on Linux

        // Folders for new profiles sit next to the default one.
        public static string ProfilesRoot { get { return Path.GetDirectoryName(DefaultDir); } }

        // The newest installed Desktop, or null. On Windows, Squirrel keeps a few versions
        // side by side in app-x.y.z folders.
        public static string Exe { get { return FindExe(); } }

        public static bool Found { get { return Exe != null; } }

        public static string WhereWeLook
        {
            get
            {
                if (OperatingSystem.IsWindows()) return Path.Combine(LocalAppData, "GitHubDesktop", "app-*", "GitHubDesktop.exe");
                if (OperatingSystem.IsMacOS()) return MacApp;
                return "github-desktop on your PATH, or the " + Flatpak + " Flatpak";
            }
        }

        static string FindExe()
        {
            try
            {
                if (OperatingSystem.IsWindows())
                {
                    string root = Path.Combine(LocalAppData, "GitHubDesktop");
                    if (!Directory.Exists(root)) return null;
                    return Directory.GetDirectories(root, "app-*")
                        .Select(d => new { d, v = ParseVersion(Path.GetFileName(d).Substring(4)) })
                        .Where(x => x.v != null && File.Exists(Path.Combine(x.d, "GitHubDesktop.exe")))
                        .OrderByDescending(x => x.v)
                        .Select(x => Path.Combine(x.d, "GitHubDesktop.exe"))
                        .FirstOrDefault();
                }
                if (OperatingSystem.IsMacOS()) return Directory.Exists(MacApp) ? MacApp : null;
                foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(':'))
                {
                    string f = Path.Combine(dir, "github-desktop");
                    if (File.Exists(f)) return f;
                }
                foreach (var f in new[] { "/var/lib/flatpak/exports/bin/" + Flatpak, Path.Combine(Home, ".local/share/flatpak/exports/bin/" + Flatpak) })
                    if (File.Exists(f)) return f;
            }
            catch { }
            return null;
        }

        static Version ParseVersion(string s) { Version v; return Version.TryParse(s, out v) ? v : null; }

        // Squirrel's launcher stub, which stays put across updates (for shortcut icons).
        public static string WindowsStub { get { return Path.Combine(LocalAppData, "GitHubDesktop", "GitHubDesktop.exe"); } }

        // ---------- starting ----------

        // Starts the profile's Desktop, or brings it forward if it's running. 'link' is an
        // x-github-desktop-auth:// sign-in link to hand it; 'gitConfig' is the file its git
        // should treat as the global config (null = the real one).
        public static void Launch(Profile p, string link, string gitConfig)
        {
            string exe = Exe;
            if (exe == null) throw new InvalidOperationException("GitHub Desktop isn't installed.");
            bool custom = !Profiles.IsDefault(p);
            if (custom) Directory.CreateDirectory(p.dataDir);

            ProcessStartInfo psi;
            if (OperatingSystem.IsMacOS())
            {
                // 'open -n' starts another copy; Electron hands it to the running one if that
                // profile is already open.
                psi = new ProcessStartInfo("open");
                if (custom) psi.ArgumentList.Add("-n");
                psi.ArgumentList.Add("-a");
                psi.ArgumentList.Add(exe);
                if (gitConfig != null) { psi.ArgumentList.Add("--env"); psi.ArgumentList.Add("GIT_CONFIG_GLOBAL=" + gitConfig); }
                psi.ArgumentList.Add("--args");
            }
            else if (exe.EndsWith(Flatpak, StringComparison.Ordinal))
            {
                psi = new ProcessStartInfo("flatpak");
                psi.ArgumentList.Add("run");
                if (gitConfig != null) psi.ArgumentList.Add("--env=GIT_CONFIG_GLOBAL=" + gitConfig);
                psi.ArgumentList.Add(Flatpak);
            }
            else
            {
                psi = new ProcessStartInfo(exe);
                if (gitConfig != null) psi.Environment["GIT_CONFIG_GLOBAL"] = gitConfig;
            }
            if (custom) psi.ArgumentList.Add("--user-data-dir=" + p.dataDir);
            if (link != null)
            {
                if (OperatingSystem.IsWindows()) psi.ArgumentList.Add("--protocol-launcher");
                psi.ArgumentList.Add(link);
            }
            psi.UseShellExecute = false;
            psi.WorkingDirectory = Home;
            Process.Start(psi);
        }

        // ---------- running-instance detection ----------

        // Data folders whose Desktop main process is alive. On Windows 'pids', if given, is
        // filled with each main process's id and data folder (for Retitle).
        public static HashSet<string> RunningDirs(Dictionary<uint, string> pids = null)
        {
            var set = new HashSet<string>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
            try
            {
                if (OperatingSystem.IsWindows())
                {
                    foreach (var kv in WindowsCommandLines())
                    {
                        if (!IsMain(kv.Value)) continue;
                        string dir = Trim(DataDirIn(kv.Value) ?? DefaultDir);
                        set.Add(dir);
                        if (pids != null) pids[kv.Key] = dir;
                    }
                }
                else foreach (var cmd in MainCommandLines()) set.Add(Trim(DataDirIn(cmd) ?? DefaultDir));
            }
            catch { }
            return set;
        }

        // The --user-data-dir in a command line, or null. It comes three ways:
        //   "--user-data-dir=C:\a b"   the whole argument quoted (how Launch passes a path with spaces)
        //   --user-data-dir="C:\a b"   just the value quoted (Electron's helpers)
        //   --user-data-dir=/a b       unquoted (ps on macOS and Linux): up to the next --switch
        static readonly Regex DataDirArg = new Regex("(\")?--user-data-dir=(?:\"([^\"]+)\"|(?(1)([^\"]+)|(.+?)(?=\\s--|$)))");

        public static string DataDirIn(string cmd)
        {
            var m = DataDirArg.Match(cmd);
            if (!m.Success) return null;
            for (int g = 2; g <= 4; g++) if (m.Groups[g].Success) return m.Groups[g].Value.Trim();
            return null;
        }

        public static string Trim(string dir) { return dir.TrimEnd('\\', '/'); }

        // Command lines of Desktop's main processes: not Chromium's helpers (--type=), and
        // not the short-lived copies that only pass a link along (--protocol-launcher).
        static IEnumerable<string> MainCommandLines()
        {
            IEnumerable<string> all;
            if (OperatingSystem.IsWindows()) all = WindowsCommandLines().Select(x => x.Value);
            else if (OperatingSystem.IsMacOS()) all = MacCommandLines();
            else all = LinuxCommandLines();
            return all.Where(IsMain);
        }

        static bool IsMain(string cmd) { return !cmd.Contains("--type=") && !cmd.Contains("--protocol-launcher"); }

        // Process id -> command line for every GitHubDesktop.exe.
        [System.Runtime.Versioning.SupportedOSPlatform("windows")]
        public static Dictionary<uint, string> WindowsCommandLines()
        {
            var d = new Dictionary<uint, string>();
            using (var q = new ManagementObjectSearcher("SELECT ProcessId, CommandLine FROM Win32_Process WHERE Name='GitHubDesktop.exe'"))
            {
                foreach (ManagementObject pr in q.Get())
                {
                    string cmd = pr["CommandLine"] as string;
                    if (!string.IsNullOrEmpty(cmd)) d[(uint)pr["ProcessId"]] = cmd;
                }
            }
            return d;
        }

        static IEnumerable<string> MacCommandLines()
        {
            var psi = new ProcessStartInfo("ps", "-axww -o args=") { RedirectStandardOutput = true, UseShellExecute = false };
            using (var p = Process.Start(psi))
            {
                string all = p.StandardOutput.ReadToEnd();
                p.WaitForExit(3000);
                return all.Split('\n').Where(l => l.Contains("GitHub Desktop.app/Contents/MacOS/GitHub Desktop")).ToList();
            }
        }

        static IEnumerable<string> LinuxCommandLines()
        {
            var list = new List<string>();
            foreach (var dir in Directory.GetDirectories("/proc"))
            {
                if (!char.IsDigit(Path.GetFileName(dir)[0])) continue;
                try
                {
                    var args = File.ReadAllText(Path.Combine(dir, "cmdline")).Split('\0');
                    if (args.Length > 0 && Path.GetFileName(args[0]).StartsWith("github-desktop", StringComparison.Ordinal))
                        list.Add(string.Join(" ", args.Where(a => a.Length > 0)));
                }
                catch { }
            }
            return list;
        }

        // ---------- window titles ----------

        // Every copy of Desktop calls its window "GitHub Desktop". This renames each running
        // profile's window to Profiles.WindowTitle, so they can be told apart in the taskbar
        // and Alt+Tab. Windows only for now. 'pids' is from RunningDirs (null = look them
        // up). Returns how many windows were found, and how many of those needed renaming.
        public static Tuple<int, int> Retitle(IEnumerable<Profile> profiles, Dictionary<uint, string> pids = null)
        {
            if (!OperatingSystem.IsWindows()) return Tuple.Create(0, 0);
            int found = 0, changed = 0;
            try
            {
                var want = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var p in profiles) want[Trim(Profiles.DataDirOf(p))] = Profiles.WindowTitle(p);
                if (pids == null) { pids = new Dictionary<uint, string>(); RunningDirs(pids); }
                var titles = new Dictionary<uint, string>();
                foreach (var kv in pids)
                {
                    string t;
                    if (want.TryGetValue(kv.Value, out t)) titles[kv.Key] = t;
                }
                if (titles.Count == 0) return Tuple.Create(0, 0);

                var text = new StringBuilder(512);
                EnumWindows((h, _) =>
                {
                    uint pid;
                    string t;
                    GetWindowThreadProcessId(h, out pid);
                    if (!titles.TryGetValue(pid, out t) || !IsWindowVisible(h)) return true;
                    text.Clear();
                    GetWindowText(h, text, text.Capacity);
                    if (text.Length == 0) return true;   // Electron's untitled helper windows
                    found++;
                    if (text.ToString() != t)
                    {
                        // Not SetWindowText: that waits forever on a window that's hung.
                        IntPtr r;
                        SendMessageTimeout(h, WM_SETTEXT, IntPtr.Zero, t, SMTO_ABORTIFHUNG, 1000, out r);
                        changed++;
                    }
                    return true;
                }, IntPtr.Zero);
            }
            catch { }
            return Tuple.Create(found, changed);
        }

        delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);
        const uint WM_SETTEXT = 0x000C, SMTO_ABORTIFHUNG = 0x0002;
        [DllImport("user32.dll")] static extern bool EnumWindows(EnumWindowsProc proc, IntPtr lParam);
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
        [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hwnd);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int max);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern IntPtr SendMessageTimeout(IntPtr hwnd, uint msg, IntPtr wParam, string lParam, uint flags, uint timeout, out IntPtr result);
    }
}
