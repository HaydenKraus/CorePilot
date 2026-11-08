using System.Runtime.InteropServices;
using CorePilot.App.Localization;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;
using WpfApp = System.Windows.Application;
using WpfWindowState = System.Windows.WindowState;

namespace CorePilot.App.Services;

/// <summary>Notify icon with a runtime-drawn icon and a small context menu.</summary>
public sealed class TrayService : IDisposable
{
    private readonly Forms.NotifyIcon _icon;
    private readonly Forms.ToolStripMenuItem _openItem;
    private readonly Forms.ToolStripMenuItem _keepItem;
    private readonly Forms.ToolStripMenuItem _exitItem;
    private IntPtr _iconHandle;
    private bool _disposed;

    public TrayService()
    {
        _openItem = new Forms.ToolStripMenuItem(Text("tray.open"));
        _keepItem = new Forms.ToolStripMenuItem(Text("tray.keepRules"));
        _exitItem = new Forms.ToolStripMenuItem(Text("tray.exit"));

        var menu = new Forms.ContextMenuStrip { ShowImageMargin = false };
        menu.Items.Add(_openItem);
        menu.Items.Add(_keepItem);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(_exitItem);

        _icon = new Forms.NotifyIcon
        {
            Text = "CorePilot",
            ContextMenuStrip = menu,
            Icon = CreateAppIcon()
        };

        _openItem.Click += (_, _) => ShowMainWindow();
        _keepItem.Click += (_, _) => ToggleKeepEnforced();
        _exitItem.Click += (_, _) => ExitApplication();
        _icon.DoubleClick += (_, _) => ShowMainWindow();

        LocalizationService.Instance.PropertyChanged += OnLocalizationChanged;
        UpdateKeepState();
        _icon.Visible = true;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        LocalizationService.Instance.PropertyChanged -= OnLocalizationChanged;

        _icon.Visible = false;
        _icon.Dispose();

        if (_iconHandle != IntPtr.Zero)
        {
            DestroyIcon(_iconHandle);
            _iconHandle = IntPtr.Zero;
        }
    }

    /// <summary>Public entry point used by the "Open" item and by double click.</summary>
    public static void ShowMainWindow()
    {
        var window = WpfApp.Current?.MainWindow;
        if (window is null)
        {
            return;
        }

        window.Show();
        if (window.WindowState == WpfWindowState.Minimized)
        {
            window.WindowState = WpfWindowState.Normal;
        }

        window.Activate();
        window.Topmost = true;
        window.Topmost = false;
        window.Focus();
    }

    private void ToggleKeepEnforced()
    {
        var settings = AppServices.Settings;
        if (settings is null)
        {
            return;
        }

        settings.Settings.KeepRulesEnforced = !settings.Settings.KeepRulesEnforced;
        settings.Save();
        UpdateKeepState();
    }

    private void UpdateKeepState()
    {
        var settings = AppServices.Settings;
        _keepItem.Checked = settings is null || settings.Settings.KeepRulesEnforced;
    }

    private static void ExitApplication()
    {
        var app = WpfApp.Current;
        if (app is null)
        {
            return;
        }

        MainWindow.ExitRequested = true;
        app.Shutdown();
    }

    private void OnLocalizationChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (_disposed)
        {
            return;
        }

        _openItem.Text = Text("tray.open");
        _keepItem.Text = Text("tray.keepRules");
        _exitItem.Text = Text("tray.exit");
    }

    private static string Text(string key) => LocalizationService.Instance[key];

    private Drawing.Icon CreateAppIcon()
    {
        try
        {
            var path = Environment.ProcessPath;
            if (!string.IsNullOrWhiteSpace(path))
            {
                var icon = Drawing.Icon.ExtractAssociatedIcon(path);
                if (icon is not null)
                {
                    return icon;
                }
            }
        }
        catch
        {
            // Fall back to the drawn glyph below.
        }

        return CreateRuntimeIcon();
    }

    private Drawing.Icon CreateRuntimeIcon()
    {
        using var bitmap = new Drawing.Bitmap(32, 32);
        using var graphics = Drawing.Graphics.FromImage(bitmap);
        graphics.SmoothingMode = Drawing.Drawing2D.SmoothingMode.AntiAlias;
        graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
        graphics.Clear(Drawing.Color.Transparent);

        using var background = new Drawing.SolidBrush(Drawing.Color.FromArgb(255, 59, 124, 255));
        graphics.FillEllipse(background, 1, 1, 30, 30);

        // Four "core" dots on top, big C in the middle would crowd a 16px tray glyph,
        // so keep a clean white C.
        using var font = new Drawing.Font("Segoe UI", 19, Drawing.FontStyle.Bold, Drawing.GraphicsUnit.Pixel);
        using var format = new Drawing.StringFormat
        {
            Alignment = Drawing.StringAlignment.Center,
            LineAlignment = Drawing.StringAlignment.Center
        };

        graphics.DrawString("C", font, Drawing.Brushes.White, new Drawing.RectangleF(0, 1, 32, 32), format);

        var handle = bitmap.GetHicon();
        _iconHandle = handle;
        return Drawing.Icon.FromHandle(handle);
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr handle);
}
