using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
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

        // Where Desktop keeps its data when it's started normally. The Flatpak has its own
        // ~/.config, under ~/.var/app.
        public static readonly string DefaultDir =
            OperatingSystem.IsMacOS() ? Path.Combine(Home, "Library", "Application Support", "GitHub Desktop")
            : IsFlatpak(FindExe()) ? Path.Combine(Home, ".var", "app", Flatpak, "config", "GitHub Desktop")
            : Path.Combine(AppData, "GitHub Desktop");   // %APPDATA% on Windows, ~/.config on Linux

        static bool IsFlatpak(string exe) { return exe != null && exe.EndsWith(Flatpak, StringComparison.Ordinal); }

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
            else if (IsFlatpak(exe))
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

        // Data folders whose Desktop main process is alive.
        public static HashSet<string> RunningDirs()
        {
            var set = new HashSet<string>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
            try { foreach (var cmd in MainProcesses().Values) set.Add(DirOf(cmd)); }
            catch { }
            return set;
        }

        // The main process of the Desktop using 'dataDir', or 0.
        public static uint MainPidOf(string dataDir)
        {
            try
            {
                string want = Trim(dataDir);
                foreach (var kv in MainProcesses())
                    if (string.Equals(DirOf(kv.Value), want, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)) return kv.Key;
            }
            catch { }
            return 0;
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

        // The data folder a Desktop command line uses: its --user-data-dir, or Desktop's own default.
        static string DirOf(string cmd)
        {
            return Trim(DataDirIn(cmd) ?? DefaultDir);
        }

        public static string Trim(string dir) { return dir.TrimEnd('\\', '/'); }

        // Process id -> command line of Desktop's main processes: not Chromium's helpers
        // (--type=), and not the short-lived copies that only pass a link along
        // (--protocol-launcher on Windows, the bare link on Linux).
        static Dictionary<uint, string> MainProcesses()
        {
            if (OperatingSystem.IsWindows())
            {
                // A launcher becomes the main process itself when no default copy was open to
                // take its link: then it has Chromium helpers of its own.
                var all = WindowsProcesses();
                var parents = new HashSet<uint>(all.Values.Where(x => x.Cmd.Contains("--type=")).Select(x => x.Parent));
                return all.Where(x => !x.Value.Cmd.Contains("--type=") && (!x.Value.Cmd.Contains("--protocol-launcher") || parents.Contains(x.Key)))
                    .ToDictionary(x => x.Key, x => x.Value.Cmd);
            }
            var others = OperatingSystem.IsMacOS() ? MacCommandLines() : LinuxCommandLines();
            return others.Where(x => !x.Value.Contains("--type=") && !x.Value.Contains("x-github-desktop-auth://"))
                .ToDictionary(x => x.Key, x => x.Value);
        }

        public class WinProcess
        {
            public string Cmd;
            public uint Parent;
            public DateTime StartedUtc;
        }

        // Every GitHubDesktop.exe: its command line, parent and start time.
        [System.Runtime.Versioning.SupportedOSPlatform("windows")]
        public static Dictionary<uint, WinProcess> WindowsProcesses()
        {
            var d = new Dictionary<uint, WinProcess>();
            using (var q = new ManagementObjectSearcher("SELECT ProcessId, ParentProcessId, CreationDate, CommandLine FROM Win32_Process WHERE Name='GitHubDesktop.exe'"))
            {
                foreach (ManagementObject pr in q.Get())
                {
                    string cmd = pr["CommandLine"] as string;
                    if (string.IsNullOrEmpty(cmd)) continue;
                    DateTime started = DateTime.MinValue;
                    try { started = ManagementDateTimeConverter.ToDateTime((string)pr["CreationDate"]).ToUniversalTime(); } catch { }
                    d[(uint)pr["ProcessId"]] = new WinProcess { Cmd = cmd, Parent = (uint)pr["ParentProcessId"], StartedUtc = started };
                }
            }
            return d;
        }

        // Process id -> command line for every GitHubDesktop.exe.
        [System.Runtime.Versioning.SupportedOSPlatform("windows")]
        public static Dictionary<uint, string> WindowsCommandLines()
        {
            return WindowsProcesses().ToDictionary(x => x.Key, x => x.Value.Cmd);
        }

        // When a Linux process started (UTC), from /proc/<pid>/stat; MinValue if unknown.
        public static DateTime LinuxStartedUtc(uint pid)
        {
            try
            {
                // Field 22 is the start time in clock ticks since boot; the name in field 2
                // can contain spaces, so count from its closing parenthesis.
                string stat = File.ReadAllText("/proc/" + pid + "/stat");
                var fields = stat.Substring(stat.LastIndexOf(')') + 2).Split(' ');
                long ticks = long.Parse(fields[19]);
                long boot = long.Parse(File.ReadAllLines("/proc/stat").First(l => l.StartsWith("btime ")).Substring(6));
                return DateTimeOffset.FromUnixTimeSeconds(boot).UtcDateTime.AddSeconds(ticks / 100.0);   // USER_HZ is 100 on Linux
            }
            catch { return DateTime.MinValue; }
        }

        static Dictionary<uint, string> MacCommandLines()
        {
            var d = new Dictionary<uint, string>();
            var psi = new ProcessStartInfo("ps", "-axww -o pid=,args=") { RedirectStandardOutput = true, UseShellExecute = false };
            using (var p = Process.Start(psi))
            {
                string all = p.StandardOutput.ReadToEnd();
                p.WaitForExit(3000);
                foreach (var line in all.Split('\n'))
                {
                    var m = Regex.Match(line, "^\\s*(\\d+)\\s+(.*GitHub Desktop\\.app/Contents/MacOS/GitHub Desktop.*)$");
                    if (m.Success) d[uint.Parse(m.Groups[1].Value)] = m.Groups[2].Value;
                }
            }
            return d;
        }

        // Chromium rewrites its processes' /proc/<pid>/cmdline as one space-separated string
        // (for the process title), so match the program at the start of the joined line:
        // /usr/lib/github-desktop/github-desktop, or /app/github-desktop/github-desktop in the
        // Flatpak (not bwrap or flatpak themselves).
        static readonly Regex LinuxDesktopExe = new Regex("^(?:\\S*/)?github-desktop(?:\\s|$)");

        static Dictionary<uint, string> LinuxCommandLines()
        {
            return LinuxProcesses()
                .Select(x => new KeyValuePair<uint, string>(x.Key, string.Join(" ", x.Value)))
                .Where(x => LinuxDesktopExe.IsMatch(x.Value))
                .ToDictionary(x => x.Key, x => x.Value);
        }

        // Process id -> arguments (argv[0] first) for every process we can see in /proc.
        public static Dictionary<uint, string[]> LinuxProcesses()
        {
            var d = new Dictionary<uint, string[]>();
            foreach (var dir in Directory.GetDirectories("/proc"))
            {
                uint pid;
                if (!uint.TryParse(Path.GetFileName(dir), out pid)) continue;
                try { d[pid] = File.ReadAllText(Path.Combine(dir, "cmdline")).Split('\0').Where(a => a.Length > 0).ToArray(); }
                catch { }
            }
            return d;
        }
    }
}
