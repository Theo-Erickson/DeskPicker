using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace DeskPicker
{
    public class Profile
    {
        public string name { get; set; }
        public string dataDir { get; set; }       // "" = Desktop's default folder
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string githubLogin { get; set; }   // the account this profile signs in as (just a label)
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string gitName { get; set; }       // commit identity; null = the global git config's
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string gitEmail { get; set; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string windowTitle { get; set; }   // its Desktop window's title; null = "GitHub Desktop (name)"
    }

    // The profile list (profiles.json next to the app) and the things you can do to a
    // profile's folder. Removing a profile here never deletes its data folder.
    static class Profiles
    {
        static readonly string ProfilesFile = Path.Combine(AppContext.BaseDirectory, "profiles.json");

        public static List<Profile> Load()
        {
            try
            {
                if (File.Exists(ProfilesFile))
                {
                    var loaded = JsonSerializer.Deserialize<List<Profile>>(File.ReadAllText(ProfilesFile));
                    if (loaded != null && loaded.Count > 0) return loaded;
                }
            }
            catch { }
            var fresh = new List<Profile> { new Profile { name = "Main", dataDir = "" } };
            Save(fresh);
            return fresh;
        }

        public static void Save(List<Profile> profiles)
        {
            File.WriteAllText(ProfilesFile, JsonSerializer.Serialize(profiles, new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
        }

        public static string DataDirOf(Profile p) { return IsDefault(p) ? Desktop.DefaultDir : p.dataDir; }

        // What the profile's Desktop window is called, so the copies can be told apart in the
        // taskbar and Alt+Tab.
        public static string WindowTitle(Profile p)
        {
            return string.IsNullOrWhiteSpace(p.windowTitle) ? DefaultWindowTitle(p.name) : p.windowTitle.Trim();
        }

        public static string DefaultWindowTitle(string name) { return "GitHub Desktop (" + (name ?? "").Trim() + ")"; }

        public static bool IsDefault(Profile p) { return string.IsNullOrEmpty(p.dataDir); }

        public static bool IsRunning(HashSet<string> running, Profile p) { return running.Contains(Desktop.Trim(DataDirOf(p))); }

        // A data folder for a new profile called 'name', next to Desktop's own.
        public static string NewDataDir(string name, int count)
        {
            string folder = "GitHub Desktop-" + Regex.Replace(name ?? "", "[^A-Za-z0-9_-]", "");
            if (folder == "GitHub Desktop-") folder = "GitHub Desktop-Profile" + (count + 1);
            return Path.Combine(Desktop.ProfilesRoot, folder);
        }

        // True if the profile lives somewhere the system may clean out (the temp folder).
        public static bool InTemp(Profile p)
        {
            if (IsDefault(p)) return false;
            string temp = Desktop.Trim(Path.GetFullPath(Path.GetTempPath()));
            string dir = Desktop.Trim(Path.GetFullPath(p.dataDir));
            return dir.StartsWith(temp + Path.DirectorySeparatorChar, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
        }

        // Moves a (closed) profile's data folder to 'to'. Its sign-in stays: the token is in
        // the system keychain under the account's login, not in the folder.
        public static void MoveData(Profile p, string to)
        {
            if (Directory.Exists(to)) throw new IOException(to + " already exists.");
            string from = p.dataDir;
            try { Directory.Move(from, to); }
            catch (IOException)
            {
                // Another drive: copy, then remove the original.
                CopyTree(from, to);
                Directory.Delete(from, true);
            }
            p.dataDir = to;
        }

        static void CopyTree(string from, string to)
        {
            Directory.CreateDirectory(to);
            foreach (var f in Directory.GetFiles(from)) File.Copy(f, Path.Combine(to, Path.GetFileName(f)));
            foreach (var d in Directory.GetDirectories(from)) CopyTree(d, Path.Combine(to, Path.GetFileName(d)));
        }

        public static void OpenFolder(string dir)
        {
            if (OperatingSystem.IsWindows()) Process.Start("explorer.exe", "\"" + dir + "\"");
            else Process.Start(OperatingSystem.IsMacOS() ? "open" : "xdg-open", "\"" + dir + "\"");
        }

        // Makes a shortcut that opens the profile through DeskPicker (so its commit identity
        // comes along) and returns where it went. Null if this system isn't supported yet.
        public static string CreateShortcut(Profile p)
        {
            string safe = Regex.Replace(WindowTitle(p), "[\\\\/:*?\"<>|]", "").Trim();
            string self = Environment.ProcessPath;
            if (OperatingSystem.IsWindows())
            {
                string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), safe + ".lnk");
                dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell"));
                dynamic lnk = shell.CreateShortcut(path);
                lnk.TargetPath = self;
                lnk.Arguments = "--open \"" + p.name + "\"";
                lnk.WorkingDirectory = Path.GetDirectoryName(self);
                if (File.Exists(Desktop.WindowsStub)) lnk.IconLocation = Desktop.WindowsStub + ",0";
                lnk.Save();
                return "your desktop, as " + safe;
            }
            if (OperatingSystem.IsLinux())
            {
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share", "applications");
                Directory.CreateDirectory(dir);
                string path = Path.Combine(dir, "deskpicker-" + Regex.Replace(safe.ToLowerInvariant(), "[^a-z0-9]+", "-") + ".desktop");
                File.WriteAllText(path,
                    "[Desktop Entry]\nType=Application\nName=" + safe + "\n" +
                    "Exec=\"" + self + "\" --open \"" + p.name.Replace("\"", "") + "\"\nIcon=github-desktop\nTerminal=false\n");
                return "your app menu, as " + safe;
            }
            return null;
        }
    }
}
