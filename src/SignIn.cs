using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls.ApplicationLifetimes;

namespace DeskPicker
{
    // Getting the browser's sign-in back to the right copy of Desktop.
    //
    // When you sign in, Desktop opens github.com in the browser, which ends by opening an
    // x-github-desktop-auth:// link. The system always gives that link to the default copy
    // (on Windows, explorer.exe even caches the handler, so re-registering it doesn't help).
    // The default copy rejects it, since it didn't start that sign-in, and the one-time code
    // in it stays unused. So while a profile is waiting, DeskPicker watches for the short-lived
    // "GitHubDesktop.exe --protocol-launcher <link>" process the system starts, and hands
    // the same link to the waiting profile. The link is never written down anywhere.
    static class SignIn
    {
        static readonly TimeSpan Patience = TimeSpan.FromMinutes(10);

        // Which profile is waiting for its browser sign-in, if any: the one that most
        // recently started one (per its own log) without receiving a link since. Null when
        // nobody is, or when it's the default copy, which gets its link anyway.
        public static Pending Waiting(List<Profile> profiles, HashSet<string> running)
        {
            Pending best = null;
            foreach (var p in profiles)
            {
                if (!Profiles.IsRunning(running, p)) continue;
                DateTime started, received;
                if (!ReadLog(Profiles.DataDirOf(p), out started, out received)) continue;
                if (started <= received || DateTime.UtcNow - started > Patience) continue;
                if (best == null || started > best.Since) best = new Pending { Profile = p, Since = started };
            }
            return best != null && !Profiles.IsDefault(best.Profile) ? best : null;
        }

        public class Pending
        {
            public Profile Profile;
            public DateTime Since;   // when it started signing in (UTC)
        }

        // The last "started a browser sign-in" and "received a link" times in today's log.
        static bool ReadLog(string dataDir, out DateTime started, out DateTime received)
        {
            started = received = DateTime.MinValue;
            try
            {
                var log = new DirectoryInfo(Path.Combine(dataDir, "logs")).GetFiles("*.desktop.production.log")
                    .OrderByDescending(f => f.LastWriteTimeUtc).FirstOrDefault();
                if (log == null) return false;
                foreach (var line in Tail(log.FullName, 256 * 1024))
                {
                    if (line.Contains("[SignInStore] initializing OAuth flow")) started = Stamp(line, started);
                    else if (line.Contains("Processing protocol url")) received = Stamp(line, received);
                }
                return true;
            }
            catch { return false; }
        }

        // Lines start "2026-10-30T21:49:02.146Z - info: ...".
        static DateTime Stamp(string line, DateTime fallback)
        {
            DateTime t;
            int dash = line.IndexOf(" - ", StringComparison.Ordinal);
            if (dash > 0 && DateTime.TryParse(line.Substring(0, dash), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out t)) return t;
            return fallback;
        }

        // The last 'bytes' of a file Desktop still has open, as lines.
        static string[] Tail(string path, int bytes)
        {
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                long start = Math.Max(0, fs.Length - bytes);
                fs.Seek(start, SeekOrigin.Begin);
                var buf = new byte[fs.Length - start];
                int read = 0;
                while (read < buf.Length) { int n = fs.Read(buf, read, buf.Length - read); if (n <= 0) break; read += n; }
                return Encoding.UTF8.GetString(buf, 0, read).Split('\n');
            }
        }

        // ---------- passing the link along ----------

        // On Windows and Linux the link passes through a process's command line, where we
        // can read it. On macOS it's an Apple Event to the registered app, so DeskPicker has
        // to be an .app that can register itself (see Mac).
        public static bool CanRelay
        {
            get { return OperatingSystem.IsWindows() || OperatingSystem.IsLinux() || (OperatingSystem.IsMacOS() && Mac.OwnBundle != null); }
        }

        static readonly object gate = new object();
        static CancellationTokenSource watching;
        static Pending watched;

        // Called when a link was handed to a profile (on a worker thread).
        public static event Action<Profile> Relayed;

        // Watches for the link while 'pending' is waiting; null stops watching.
        public static void Watch(Pending pending)
        {
            lock (gate)
            {
                if (pending != null && watched != null && pending.Profile == watched.Profile && pending.Since == watched.Since) return;
                bool wasWatching = watching != null;
                watching?.Cancel();
                watching = null;
                watched = pending;
                if (pending == null || !CanRelay)
                {
                    if (OperatingSystem.IsMacOS() && wasWatching) Mac.SetHandler(Mac.DesktopBundleId);
                    return;
                }
                var cts = new CancellationTokenSource(Patience);
                watching = cts;
                if (OperatingSystem.IsMacOS()) Task.Run(() => { if (OperatingSystem.IsMacOS()) HoldHandler(cts.Token); });
                else Task.Run(() => Relay(pending.Profile, pending.Since, cts.Token));
            }
        }

        // ---------- macOS ----------

        // While a profile is waiting, DeskPicker is the app that opens sign-in links. Every
        // Desktop that starts takes the links back, so keep claiming them; give them back
        // to Desktop when done (or after 'Patience').
        [System.Runtime.Versioning.SupportedOSPlatform("macos")]
        static void HoldHandler(CancellationToken stop)
        {
            try { Mac.Register(Mac.OwnBundle); } catch { }
            while (!stop.IsCancellationRequested)
            {
                try { Mac.SetHandler(OwnBundleId); } catch { }
                stop.WaitHandle.WaitOne(1000);
            }
            lock (gate) { if (watching == null || watching.Token == stop) try { Mac.SetHandler(Mac.DesktopBundleId); } catch { } }
        }

        // Must match CFBundleIdentifier in build.sh.
        const string OwnBundleId = "io.github.theo-erickson.deskpicker";

        // macOS opened a link with DeskPicker (on the UI thread). Hand it to the waiting
        // profile, or, if nobody is waiting any more, to the default copy that would have
        // got it anyway.
        [System.Runtime.Versioning.SupportedOSPlatform("macos")]
        public static void Opened(string link)
        {
            if (!link.StartsWith(Mac.Scheme + "://", StringComparison.OrdinalIgnoreCase)) return;
            Profile target;
            lock (gate) target = watching != null ? watched?.Profile : null;
            if (target != null)
            {
                uint pid = Desktop.MainPidOf(Profiles.DataDirOf(target));
                if (pid != 0 && Mac.SendLink((int)pid, link) == 0)
                {
                    Watch(null);
                    Relayed?.Invoke(target);
                    return;
                }
            }
            uint main = Desktop.MainPidOf(Desktop.DefaultDir);
            if (main != 0 && Mac.SendLink((int)main, link) == 0) return;
            // No default copy open: give the links back to Desktop and let macOS start it.
            Mac.SetHandler(Mac.DesktopBundleId);
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo("open") { UseShellExecute = false };
                psi.ArgumentList.Add("-b");
                psi.ArgumentList.Add(Mac.DesktopBundleId);
                psi.ArgumentList.Add(link);
                System.Diagnostics.Process.Start(psi);
            }
            catch { }
        }

        // Starts listening for links macOS opens with DeskPicker. Call once Avalonia is set up.
        [System.Runtime.Versioning.SupportedOSPlatform("macos")]
        public static void ListenForLinks()
        {
            var life = Application.Current?.TryGetFeature<IActivatableLifetime>();
            if (life == null) return;
            life.Activated += (s, e) =>
            {
                var uri = (e as ProtocolActivatedEventArgs)?.Uri;
                if (uri != null) Opened(uri.OriginalString);
            };
        }

        static readonly Regex LinkArg = new Regex("\"?(x-github-desktop-auth://[^\"\\s]+)", RegexOptions.IgnoreCase);

        // Short-lived processes that carry a link. On Windows that's "GitHubDesktop.exe
        // --protocol-launcher <link>". On Linux the link is a plain argument, and several
        // processes carry it on the way (xdg-open, then "github-desktop <link>", or
        // "flatpak run ... <link>" for the Flatpak); whichever we see first will do.
        static Dictionary<uint, string> LinkCarriers()
        {
            if (OperatingSystem.IsWindows()) return Desktop.WindowsCommandLines();
            if (OperatingSystem.IsLinux())
                return Desktop.LinuxProcesses()
                    .Select(x => new KeyValuePair<uint, string>(x.Key, string.Join(" ", x.Value)))
                    .Where(x => x.Value.Contains("x-github-desktop-auth://", StringComparison.OrdinalIgnoreCase))
                    .ToDictionary(x => x.Key, x => x.Value);
            return new Dictionary<uint, string>();
        }

        // When a link-carrying process started (UTC); MinValue if unknown.
        static DateTime StartedUtc(uint pid, Dictionary<uint, Desktop.WinProcess> windows)
        {
            if (windows != null) { Desktop.WinProcess w; return windows.TryGetValue(pid, out w) ? w.StartedUtc : DateTime.MinValue; }
            return OperatingSystem.IsLinux() ? Desktop.LinuxStartedUtc(pid) : DateTime.MinValue;
        }

        static void Relay(Profile target, DateTime since, CancellationToken stop)
        {
            HashSet<uint> seen = null;
            while (!stop.IsCancellationRequested)
            {
                Dictionary<uint, string> now;
                try { now = LinkCarriers(); }
                catch { now = new Dictionary<uint, string>(); }
                if (seen == null)
                {
                    // Carriers already there when we started watching count only if they
                    // started after the sign-in did: when you've signed in to the app before,
                    // the browser comes straight back, often before the first look. Older ones
                    // carry an earlier link (say, a launcher that became the main copy).
                    Dictionary<uint, Desktop.WinProcess> windows = null;
                    if (OperatingSystem.IsWindows()) try { windows = Desktop.WindowsProcesses(); } catch { }
                    seen = new HashSet<uint>(now.Keys.Where(pid => StartedUtc(pid, windows) < since.AddSeconds(-2)));
                }
                foreach (var kv in now)
                {
                    if (!seen.Add(kv.Key) || kv.Value.Contains("--user-data-dir")) continue;
                    var m = LinkArg.Match(kv.Value);
                    if (!m.Success) continue;
                    try { Desktop.Launch(target, m.Groups[1].Value, null); } catch { }
                    Relayed?.Invoke(target);
                    return;
                }
                // The launcher process lives for well under a second, so look often.
                stop.WaitHandle.WaitOne(50);
            }
        }
    }
}
