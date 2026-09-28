using System.Diagnostics;
using System.Drawing;
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
    private Process? _parentProcess;
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
        var exitItem = new ToolStripMenuItem("Exit");

        openItem.Click += (_, _) => OpenHomePage();
        exitItem.Click += (_, _) => RequestApplicationExit();

        menu.Items.Add(openItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(exitItem);
        return menu;
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
