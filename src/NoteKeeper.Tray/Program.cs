using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace NoteKeeper.Tray;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        var options = TrayOptions.Parse(args);
        if (options is null)
        {
            return;
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new TrayApplicationContext(options));
    }
}

internal sealed record TrayOptions(int ParentProcessId, string Url, string ExitEventName)
{
    public static TrayOptions? Parse(string[] args)
    {
        int? parentProcessId = null;
        string? url = null;
        string? exitEventName = null;

        for (var index = 0; index < args.Length - 1; index++)
        {
            switch (args[index])
            {
                case "--parent-pid" when int.TryParse(args[index + 1], out var parsedProcessId):
                    parentProcessId = parsedProcessId;
                    index++;
                    break;

                case "--url":
                    url = args[index + 1];
                    index++;
                    break;

                case "--exit-event":
                    exitEventName = args[index + 1];
                    index++;
                    break;
            }
        }

        if (!parentProcessId.HasValue ||
            string.IsNullOrWhiteSpace(url) ||
            string.IsNullOrWhiteSpace(exitEventName))
        {
            return null;
        }

        return new TrayOptions(parentProcessId.Value, url, exitEventName);
    }
}

internal sealed class TrayApplicationContext : ApplicationContext
{
    private readonly TrayOptions _options;
    private readonly NotifyIcon _notifyIcon;
    private readonly ContextMenuStrip _menu;
    private System.Windows.Forms.Timer? _parentTimer;
    private readonly Icon _icon;
    private ToolStripMenuItem? _consoleVisibilityItem;
    private Process? _parentProcess;
    private nint _consoleWindowHandle;
    private bool _disposed;

    public TrayApplicationContext(TrayOptions options)
    {
        _options = options;
        _icon = LoadApplicationIcon();
        _menu = CreateMenu();

        _notifyIcon = new NotifyIcon
        {
            Icon = _icon,
            Text = "NoteKeeper",
            ContextMenuStrip = _menu,
            Visible = true
        };
        _notifyIcon.DoubleClick += (_, _) => OpenHomePage();

        try
        {
            _parentProcess = Process.GetProcessById(options.ParentProcessId);
            _consoleWindowHandle = ParentConsoleWindow.TryGetHandle(options.ParentProcessId);
            UpdateConsoleVisibilityItem();
        }
        catch
        {
            ExitThread();
            return;
        }

        _parentTimer = new System.Windows.Forms.Timer
        {
            Interval = 1000,
            Enabled = true
        };
        _parentTimer.Tick += (_, _) =>
        {
            try
            {
                if (_parentProcess is null || _parentProcess.HasExited)
                {
                    ExitThread();
                }
            }
            catch
            {
                ExitThread();
            }
        };
    }

    private ContextMenuStrip CreateMenu()
    {
        var menu = new ContextMenuStrip();
        var openItem = new ToolStripMenuItem("Open NoteKeeper");
        _consoleVisibilityItem = new ToolStripMenuItem("Hide");
        var exitItem = new ToolStripMenuItem("Exit");

        openItem.Click += (_, _) => OpenHomePage();
        _consoleVisibilityItem.Click += (_, _) => ToggleConsoleVisibility();
        exitItem.Click += (_, _) => RequestApplicationExit();
        menu.Opening += (_, _) => UpdateConsoleVisibilityItem();

        menu.Items.Add(openItem);
        menu.Items.Add(_consoleVisibilityItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(exitItem);
        return menu;
    }

    private void ToggleConsoleVisibility()
    {
        if (_consoleWindowHandle == nint.Zero ||
            !ParentConsoleWindow.IsValid(_consoleWindowHandle))
        {
            _consoleWindowHandle = ParentConsoleWindow.TryGetHandle(_options.ParentProcessId);
        }

        if (_consoleWindowHandle == nint.Zero)
        {
            UpdateConsoleVisibilityItem();
            return;
        }

        if (ParentConsoleWindow.IsVisible(_consoleWindowHandle))
        {
            ParentConsoleWindow.Hide(_consoleWindowHandle);
        }
        else
        {
            ParentConsoleWindow.Show(_consoleWindowHandle);
        }

        UpdateConsoleVisibilityItem();
    }

    private void UpdateConsoleVisibilityItem()
    {
        if (_consoleVisibilityItem is null)
        {
            return;
        }

        if (_consoleWindowHandle == nint.Zero ||
            !ParentConsoleWindow.IsValid(_consoleWindowHandle))
        {
            _consoleWindowHandle = ParentConsoleWindow.TryGetHandle(_options.ParentProcessId);
        }

        var hasConsoleWindow = _consoleWindowHandle != nint.Zero;
        _consoleVisibilityItem.Enabled = hasConsoleWindow;
        _consoleVisibilityItem.Text =
            hasConsoleWindow && ParentConsoleWindow.IsVisible(_consoleWindowHandle)
                ? "Hide"
                : "Show";
    }

    private void OpenHomePage()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = _options.Url,
                UseShellExecute = true
            });
        }
        catch
        {
            // The tray helper must never terminate the NoteKeeper web application.
        }
    }

    private void RequestApplicationExit()
    {
        try
        {
            using var exitEvent = EventWaitHandle.OpenExisting(_options.ExitEventName);
            exitEvent.Set();
        }
        catch
        {
            // If the parent is already stopping, closing the tray helper is enough.
        }

        ExitThread();
    }

    protected override void ExitThreadCore()
    {
        DisposeResources();
        base.ExitThreadCore();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            DisposeResources();
        }

        base.Dispose(disposing);
    }

    private void DisposeResources()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_notifyIcon is not null)
        {
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
        }

        _parentTimer?.Stop();
        _parentTimer?.Dispose();
        _menu?.Dispose();
        _parentProcess?.Dispose();
        _parentProcess = null;
        _icon?.Dispose();
    }

    private static Icon LoadApplicationIcon()
    {
        if (!string.IsNullOrWhiteSpace(Environment.ProcessPath))
        {
            try
            {
                var icon = Icon.ExtractAssociatedIcon(Environment.ProcessPath);
                if (icon is not null)
                {
                    return icon;
                }
            }
            catch
            {
                // Fall back to a built-in icon if the executable icon cannot be loaded.
            }
        }

        return (Icon)SystemIcons.Application.Clone();
    }
}

internal static class ParentConsoleWindow
{
    private const int SwHide = 0;
    private const int SwShow = 5;

    public static nint TryGetHandle(int parentProcessId)
    {
        if (!OperatingSystem.IsWindows())
        {
            return nint.Zero;
        }

        if (!AttachConsole((uint)parentProcessId))
        {
            return nint.Zero;
        }

        try
        {
            return GetConsoleWindow();
        }
        finally
        {
            FreeConsole();
        }
    }

    public static bool IsValid(nint windowHandle) =>
        windowHandle != nint.Zero && IsWindow(windowHandle);

    public static bool IsVisible(nint windowHandle) =>
        IsValid(windowHandle) && IsWindowVisible(windowHandle);

    public static void Hide(nint windowHandle)
    {
        if (IsValid(windowHandle))
        {
            ShowWindow(windowHandle, SwHide);
        }
    }

    public static void Show(nint windowHandle)
    {
        if (IsValid(windowHandle))
        {
            ShowWindow(windowHandle, SwShow);
            SetForegroundWindow(windowHandle);
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AttachConsole(uint processId);

    [DllImport("kernel32.dll")]
    private static extern nint GetConsoleWindow();

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FreeConsole();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(nint windowHandle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(nint windowHandle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(nint windowHandle, int command);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(nint windowHandle);
}
