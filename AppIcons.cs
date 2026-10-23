using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace AppAvvio;

public static class AppIcons
{
    private static readonly Dictionary<string, ImageSource?> cache = new(StringComparer.OrdinalIgnoreCase);
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShellInfo
    {
        public IntPtr Icon;
        public int Index;
        public uint Attributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string DisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string TypeName;
    }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHGetFileInfo(string path, uint attributes, out ShellInfo info, uint size, uint flags);
    [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr icon);
    public static ImageSource? Get(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        if (cache.TryGetValue(path, out var cached)) return cached;
        var info = new ShellInfo();
        try
        {
            // Missing files use a generic application icon without asking the shell to resolve a path.
            var exists = File.Exists(path);
            SHGetFileInfo(exists ? path : "app.exe", 0x80, out info, (uint)Marshal.SizeOf<ShellInfo>(), 0x100 | (exists ? 0u : 0x10u));
            if (info.Icon == IntPtr.Zero) return null;
            var image = Imaging.CreateBitmapSourceFromHIcon(info.Icon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            image.Freeze();
            cache[path] = image;
            return image;
        }
        catch (ArgumentException) { return null; }
        finally { if (info.Icon != IntPtr.Zero) DestroyIcon(info.Icon); }
    }
}
