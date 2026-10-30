using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace DeskPicker
{
    // Which GitHub.com accounts Desktop has a saved sign-in for. Only the names of the
    // saved credentials are read, never their contents.
    static class Keychain
    {
        const string Prefix = "GitHub - https://api.github.com/";

        // Logins with a saved Desktop sign-in, or null if this system can't be checked.
        public static HashSet<string> SignedInLogins()
        {
            if (!OperatingSystem.IsWindows()) return null;
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int count;
            IntPtr list;
            if (!CredEnumerate(Prefix + "*", 0, out count, out list)) return set;   // none saved
            try
            {
                for (int i = 0; i < count; i++)
                {
                    IntPtr cred = Marshal.ReadIntPtr(list, i * IntPtr.Size);
                    // CREDENTIALW starts with two DWORDs (Flags, Type), then the TargetName pointer.
                    string target = Marshal.PtrToStringUni(Marshal.ReadIntPtr(cred, 8));
                    if (target != null && target.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
                        set.Add(target.Substring(Prefix.Length));
                }
            }
            finally { CredFree(list); }
            return set;
        }

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "CredEnumerateW")]
        static extern bool CredEnumerate(string filter, int flags, out int count, out IntPtr credentials);

        [DllImport("advapi32.dll")]
        static extern void CredFree(IntPtr buffer);
    }
}
