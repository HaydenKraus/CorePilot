using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CorePilot.App.Services;

/// <summary>Extracts real shell icons for executables and caches them per path.</summary>
internal static class ProcessIconCache
{
    private const uint SHGFI_ICON = 0x000000100;
    private const uint SHGFI_LARGEICON = 0x000000000;

    private static readonly object Gate = new();
    private static readonly Dictionary<string, ImageSource?> Cache = new(StringComparer.OrdinalIgnoreCase);

    private static ImageSource? _defaultIcon;
    private static bool _defaultLoaded;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEINFO
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szDisplayName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string szTypeName;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHGetFileInfo(string path, uint fileAttributes, ref SHFILEINFO info, uint infoSize, uint flags);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr icon);

    /// <summary>Returns a frozen icon for the executable, or the application icon as a fallback.</summary>
    public static ImageSource? Get(string? executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            return GetDefault();
        }

        lock (Gate)
        {
            if (Cache.TryGetValue(executablePath, out var cached))
            {
                return cached ?? GetDefault();
            }
        }

        var icon = Load(executablePath);
        lock (Gate)
        {
            Cache[executablePath] = icon;
        }

        return icon ?? GetDefault();
    }

    private static ImageSource? GetDefault()
    {
        lock (Gate)
        {
            if (_defaultLoaded)
            {
                return _defaultIcon;
            }
        }

        var icon = Load(Environment.ProcessPath);
        lock (Gate)
        {
            _defaultIcon = icon;
            _defaultLoaded = true;
        }

        return icon;
    }

    private static ImageSource? Load(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        try
        {
            var info = new SHFILEINFO();
            var result = SHGetFileInfo(
                path,
                0,
                ref info,
                (uint)Marshal.SizeOf<SHFILEINFO>(),
                SHGFI_ICON | SHGFI_LARGEICON);

            if (result == IntPtr.Zero || info.hIcon == IntPtr.Zero)
            {
                return null;
            }

            try
            {
                var source = Imaging.CreateBitmapSourceFromHIcon(
                    info.hIcon,
                    Int32Rect.Empty,
                    BitmapSizeOptions.FromEmptyOptions());

                // CreateBitmapSourceFromHIcon keeps the native icon alive lazily; copy the pixels
                // before the handle is destroyed, otherwise some icons render blank.
                var frozen = new WriteableBitmap(source);
                frozen.Freeze();
                return frozen;
            }
            finally
            {
                DestroyIcon(info.hIcon);
            }
        }
        catch
        {
            return null;
        }
    }
}
