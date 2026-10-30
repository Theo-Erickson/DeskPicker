using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

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

        // Only Windows so far: elsewhere the link reaches Desktop another way (macOS sends
        // it as an Apple Event), so there's nothing to read it from.
        public static bool CanRelay { get { return OperatingSystem.IsWindows(); } }

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
                watching?.Cancel();
                watching = null;
                watched = pending;
                if (pending == null || !OperatingSystem.IsWindows()) return;   // CanRelay
                var cts = new CancellationTokenSource(Patience);
                watching = cts;
                Task.Run(() => Relay(pending.Profile, cts.Token));
            }
        }

        static readonly Regex LinkArg = new Regex("--protocol-launcher\\s+\"?(x-github-desktop-auth://[^\"\\s]+)", RegexOptions.IgnoreCase);

        [System.Runtime.Versioning.SupportedOSPlatform("windows")]
        static void Relay(Profile target, CancellationToken stop)
        {
            HashSet<uint> seen = null;
            while (!stop.IsCancellationRequested)
            {
                Dictionary<uint, string> now;
                try { now = Desktop.WindowsCommandLines(); }
                catch { now = new Dictionary<uint, string>(); }
                if (seen == null) seen = new HashSet<uint>(now.Keys);   // what was already there doesn't count
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
                stop.WaitHandle.WaitOne(100);
            }
        }
    }
}
