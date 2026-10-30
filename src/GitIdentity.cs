using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace DeskPicker
{
    // A profile's commit identity. Desktop's git reads GIT_CONFIG_GLOBAL as its global
    // config, so each profile gets a small file that pulls in the real ~/.gitconfig and then
    // sets its own name and email. Desktop's "Configure Git" step writes there too, rather
    // than over your real global identity. Git on the command line isn't affected.
    static class GitIdentity
    {
        static readonly string Dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".deskpicker");

        public static bool Has(Profile p)
        {
            return !string.IsNullOrWhiteSpace(p.gitName) || !string.IsNullOrWhiteSpace(p.gitEmail);
        }

        // Named after the data folder, so renaming the profile keeps the same file.
        public static string FileFor(Profile p)
        {
            string key = Profiles.IsDefault(p) ? "default" : Regex.Replace(Path.GetFileName(Desktop.Trim(p.dataDir)), "[^A-Za-z0-9_-]", "");
            return Path.Combine(Dir, (key.Length > 0 ? key : "profile") + ".gitconfig");
        }

        // Writes the profile's config file and returns its path, or null when the profile
        // just uses the global identity.
        public static string Prepare(Profile p)
        {
            if (!Has(p)) return null;
            Directory.CreateDirectory(Dir);
            var sb = new StringBuilder();
            sb.Append("# Written by DeskPicker for the \"").Append(p.name.Replace("\"", "")).Append("\" profile; rewritten each time it opens.\n");
            sb.Append("# Change the identity in DeskPicker rather than here.\n");
            sb.Append("[include]\n\tpath = ~/.gitconfig\n");
            sb.Append("[user]\n");
            if (!string.IsNullOrWhiteSpace(p.gitName)) sb.Append("\tname = ").Append(Quote(p.gitName.Trim())).Append('\n');
            if (!string.IsNullOrWhiteSpace(p.gitEmail)) sb.Append("\temail = ").Append(Quote(p.gitEmail.Trim())).Append('\n');
            if (!string.IsNullOrWhiteSpace(p.githubLogin))
                sb.Append("[credential \"https://github.com\"]\n\tusername = ").Append(Quote(p.githubLogin.Trim())).Append('\n');
            string path = FileFor(p);
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
            return path;
        }

        static string Quote(string v)
        {
            return "\"" + v.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        }
    }
}
