using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace DeskPicker
{
    // The macOS calls the sign-in hand-off needs. macOS gives an x-github-desktop-auth://
    // link to whichever app is registered for it, as an "open this URL" Apple Event, and
    // Desktop only takes links that way (not on its command line). So while a profile is
    // waiting, DeskPicker registers itself for the link, and passes it on as the same
    // Apple Event sent to that one Desktop process.
    [SupportedOSPlatform("macos")]
    static class Mac
    {
        public const string DesktopBundleId = "com.github.GitHubClient";
        public const string Scheme = "x-github-desktop-auth";

        const string CF = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
        const string CS = "/System/Library/Frameworks/CoreServices.framework/CoreServices";

        [DllImport(CF)] static extern IntPtr CFStringCreateWithCString(IntPtr alloc, string s, uint encoding);
        [DllImport(CF)] static extern IntPtr CFURLCreateFromFileSystemRepresentation(IntPtr alloc, byte[] path, nint len, bool isDir);
        [DllImport(CF)] static extern void CFRelease(IntPtr cf);
        [DllImport(CS)] static extern int LSSetDefaultHandlerForURLScheme(IntPtr scheme, IntPtr bundleId);
        [DllImport(CS)] static extern int LSRegisterURL(IntPtr url, bool update);

        // AEDesc is 2-byte packed in the Apple Event headers.
        [StructLayout(LayoutKind.Sequential, Pack = 2)]
        struct AEDesc { public uint type; public IntPtr data; }

        [DllImport(CS)] static extern short AECreateDesc(uint type, ref int data, nint size, out AEDesc result);
        [DllImport(CS)] static extern short AECreateAppleEvent(uint cls, uint id, ref AEDesc target, short returnId, int transactionId, out AEDesc result);
        [DllImport(CS)] static extern short AEPutParamPtr(ref AEDesc ev, uint key, uint type, byte[] data, nint size);
        [DllImport(CS)] static extern int AESendMessage(ref AEDesc ev, IntPtr reply, int mode, nint timeout);
        [DllImport(CS)] static extern short AEDisposeDesc(ref AEDesc desc);

        const uint UTF8 = 0x08000100;
        static uint Code(string four) { return (uint)(four[0] << 24 | four[1] << 16 | four[2] << 8 | four[3]); }

        // The bundle DeskPicker runs from, or null when it isn't running as an .app.
        public static string OwnBundle
        {
            get
            {
                string dir = AppContext.BaseDirectory.TrimEnd('/');   // .../DeskPicker.app/Contents/MacOS
                return dir.EndsWith(".app/Contents/MacOS", StringComparison.Ordinal) ? dir.Substring(0, dir.Length - "/Contents/MacOS".Length) : null;
            }
        }

        // Makes 'bundleId' the app that opens x-github-desktop-auth:// links. Returns false if
        // macOS refused.
        public static bool SetHandler(string bundleId)
        {
            IntPtr s = CFStringCreateWithCString(IntPtr.Zero, Scheme, UTF8);
            IntPtr b = CFStringCreateWithCString(IntPtr.Zero, bundleId, UTF8);
            try { return LSSetDefaultHandlerForURLScheme(s, b) == 0; }
            finally { CFRelease(s); CFRelease(b); }
        }

        // Tells Launch Services about DeskPicker.app (and its claim on the link), in case it
        // was never opened from Finder.
        public static void Register(string bundle)
        {
            var path = Encoding.UTF8.GetBytes(bundle);
            IntPtr url = CFURLCreateFromFileSystemRepresentation(IntPtr.Zero, path, path.Length, true);
            if (url == IntPtr.Zero) return;
            try { LSRegisterURL(url, true); }
            finally { CFRelease(url); }
        }

        // Sends 'link' to one process as an "open this URL" Apple Event. Returns the
        // error code (0 = sent).
        public static int SendLink(int pid, string link)
        {
            AEDesc target, ev;
            short err = AECreateDesc(Code("kpid"), ref pid, sizeof(int), out target);
            if (err != 0) return err;
            try
            {
                err = AECreateAppleEvent(Code("GURL"), Code("GURL"), ref target, -1, 0, out ev);
                if (err != 0) return err;
                try
                {
                    var bytes = Encoding.UTF8.GetBytes(link);
                    err = AEPutParamPtr(ref ev, Code("----"), Code("utf8"), bytes, bytes.Length);
                    if (err != 0) return err;
                    return AESendMessage(ref ev, IntPtr.Zero, 1 /* kAENoReply */, -1 /* default timeout */);
                }
                finally { AEDisposeDesc(ref ev); }
            }
            finally { AEDisposeDesc(ref target); }
        }
    }
}
