using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;

namespace NoteKeeper.Services;

public sealed class WindowsTrayIconService(
    IHostApplicationLifetime applicationLifetime,
    IServer server,
    ILogger<WindowsTrayIconService> logger) : IHostedService, IDisposable
{
    private const uint WmClose = 0x0010;
    private const uint WmDestroy = 0x0002;
    private const uint WmContextMenu = 0x007B;
    private const uint WmLButtonDoubleClick = 0x0203;
    private const uint WmRButtonUp = 0x0205;
    private const uint WmApp = 0x8000;
    private const uint WmTrayIcon = WmApp + 1;

    private const uint NifMessage = 0x00000001;
    private const uint NifIcon = 0x00000002;
    private const uint NifTip = 0x00000004;
    private const uint NimAdd = 0x00000000;
    private const uint NimDelete = 0x00000002;

    private const uint MfString = 0x00000000;
    private const uint MfSeparator = 0x00000800;
    private const uint TpmRightButton = 0x0002;
    private const uint TpmReturnCommand = 0x0100;

    private const uint ImageIcon = 1;
    private const uint LrLoadFromFile = 0x00000010;
    private const uint LrDefaultSize = 0x00000040;

    private const uint OpenCommandId = 1001;
    private const uint ExitCommandId = 1002;

    private readonly object _sync = new();
    private WndProc? _windowProcedure;

    private Thread? _trayThread;
    private nint _windowHandle;
    private nint _iconHandle;
    private uint _trayThreadId;
    private uint _taskbarCreatedMessage;
    private string _windowClassName = string.Empty;
    private string _homeUrl = "http://localhost:5000";
    private bool _disposed;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
        {
            return Task.CompletedTask;
        }

        applicationLifetime.ApplicationStarted.Register(TryStartTrayThread);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        StopTrayThread();
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        StopTrayThread();
    }

    [SupportedOSPlatform("windows")]
    private void TryStartTrayThread()
    {
        try
        {
            StartTrayThread();
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Could not start the NoteKeeper system tray service.");
        }
    }

    [SupportedOSPlatform("windows")]
    private void StartTrayThread()
    {
        lock (_sync)
        {
            if (_disposed || _trayThread?.IsAlive == true)
            {
                return;
            }

            _homeUrl = ResolveHomeUrl();
            _windowProcedure ??= WindowProcedure;

            var trayThread = new Thread(TrayThreadMain)
            {
                IsBackground = true,
                Name = "NoteKeeper system tray"
            };

            trayThread.SetApartmentState(ApartmentState.STA);
            _trayThread = trayThread;
            trayThread.Start();
        }
    }

    private void StopTrayThread()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        Thread? thread;

        lock (_sync)
        {
            thread = _trayThread;

            if (_windowHandle != nint.Zero)
            {
                PostMessage(_windowHandle, WmClose, nint.Zero, nint.Zero);
            }
            else if (_trayThreadId != 0)
            {
                PostThreadMessage(_trayThreadId, 0x0012, nint.Zero, nint.Zero);
            }
        }

        if (thread is { IsAlive: true } && thread != Thread.CurrentThread)
        {
            thread.Join(TimeSpan.FromSeconds(2));
        }
    }

    [SupportedOSPlatform("windows")]
    private void TrayThreadMain()
    {
        nint moduleHandle = nint.Zero;
        var classRegistered = false;

        try
        {
            _trayThreadId = GetCurrentThreadId();
            _windowClassName = $"NoteKeeper.Tray.{Environment.ProcessId}";
            _taskbarCreatedMessage = RegisterWindowMessage("TaskbarCreated");
            moduleHandle = GetModuleHandle(null);

            var windowClass = new WindowClass
            {
                lpfnWndProc = _windowProcedure!,
                hInstance = moduleHandle,
                lpszClassName = _windowClassName
            };

            if (RegisterClass(ref windowClass) == 0)
            {
                logger.LogWarning(
                    "Could not register the NoteKeeper system tray window class. Win32 error: {Error}",
                    Marshal.GetLastWin32Error());
                return;
            }

            classRegistered = true;

            _windowHandle = CreateWindowEx(
                0,
                _windowClassName,
                "NoteKeeper",
                0,
                0,
                0,
                0,
                0,
                nint.Zero,
                nint.Zero,
                moduleHandle,
                nint.Zero);

            if (_windowHandle == nint.Zero)
            {
                logger.LogWarning(
                    "Could not create the NoteKeeper system tray window. Win32 error: {Error}",
                    Marshal.GetLastWin32Error());
                return;
            }

            _iconHandle = LoadTrayIcon();
            AddTrayIcon();

            while (GetMessage(out var message, nint.Zero, 0, 0) > 0)
            {
                TranslateMessage(ref message);
                DispatchMessage(ref message);
            }
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "The NoteKeeper system tray service stopped unexpectedly.");
        }
        finally
        {
            try
            {
                RemoveTrayIcon();

                if (_windowHandle != nint.Zero)
                {
                    DestroyWindow(_windowHandle);
                    _windowHandle = nint.Zero;
                }

                if (_iconHandle != nint.Zero)
                {
                    DestroyIcon(_iconHandle);
                    _iconHandle = nint.Zero;
                }

                if (classRegistered && moduleHandle != nint.Zero && !string.IsNullOrWhiteSpace(_windowClassName))
                {
                    UnregisterClass(_windowClassName, moduleHandle);
                }
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Could not clean up the NoteKeeper system tray service.");
            }
            finally
            {
                lock (_sync)
                {
                    _trayThreadId = 0;
                    _trayThread = null;
                }
            }
        }
    }

    [SupportedOSPlatform("windows")]
    private nint WindowProcedure(nint windowHandle, uint message, nint wParam, nint lParam)
    {
        try
        {
            if (message == WmTrayIcon)
            {
                var mouseMessage = unchecked((uint)lParam.ToInt64());

                if (mouseMessage == WmLButtonDoubleClick)
                {
                    OpenHomePage();
                    return nint.Zero;
                }

                if (mouseMessage is WmRButtonUp or WmContextMenu)
                {
                    ShowContextMenu(windowHandle);
                    return nint.Zero;
                }
            }

            if (_taskbarCreatedMessage != 0 && message == _taskbarCreatedMessage)
            {
                AddTrayIcon();
                return nint.Zero;
            }

            if (message == WmClose)
            {
                DestroyWindow(windowHandle);
                return nint.Zero;
            }

            if (message == WmDestroy)
            {
                RemoveTrayIcon();
                PostQuitMessage(0);
                return nint.Zero;
            }

            return DefWindowProc(windowHandle, message, wParam, lParam);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "An error occurred while processing a NoteKeeper tray window message.");
            return DefWindowProc(windowHandle, message, wParam, lParam);
        }
    }

    private void ShowContextMenu(nint windowHandle)
    {
        var menu = CreatePopupMenu();
        if (menu == nint.Zero)
        {
            return;
        }

        try
        {
            AppendMenu(menu, MfString, OpenCommandId, "Open NoteKeeper");
            AppendMenu(menu, MfSeparator, 0, string.Empty);
            AppendMenu(menu, MfString, ExitCommandId, "Exit");

            if (!GetCursorPos(out var point))
            {
                return;
            }

            SetForegroundWindow(windowHandle);

            var command = TrackPopupMenu(
                menu,
                TpmRightButton | TpmReturnCommand,
                point.X,
                point.Y,
                0,
                windowHandle,
                nint.Zero);

            if (command == OpenCommandId)
            {
                OpenHomePage();
            }
            else if (command == ExitCommandId)
            {
                applicationLifetime.StopApplication();
            }

            PostMessage(windowHandle, 0, nint.Zero, nint.Zero);
        }
        finally
        {
            DestroyMenu(menu);
        }
    }

    private void OpenHomePage()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = _homeUrl,
                UseShellExecute = true
            });
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Could not open the NoteKeeper home page at {Url}.", _homeUrl);
        }
    }

    private string ResolveHomeUrl()
    {
        var addresses = server.Features.Get<IServerAddressesFeature>()?.Addresses;
        var address = addresses?
            .Where(value => value.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                            value.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(value => value.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            .FirstOrDefault();

        if (string.IsNullOrWhiteSpace(address) || !Uri.TryCreate(address, UriKind.Absolute, out var uri))
        {
            return "http://localhost:5000";
        }

        var host = uri.Host is "0.0.0.0" or "::" or "*" or "+"
            ? "localhost"
            : uri.Host;

        return new UriBuilder(uri)
        {
            Host = host
        }.Uri.ToString().TrimEnd('/');
    }

    private nint LoadTrayIcon()
    {
        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "notekeeper.ico");
        if (!File.Exists(iconPath))
        {
            logger.LogWarning("The NoteKeeper tray icon was not found at {Path}.", iconPath);
            return nint.Zero;
        }

        var icon = LoadImage(
            nint.Zero,
            iconPath,
            ImageIcon,
            0,
            0,
            LrLoadFromFile | LrDefaultSize);

        if (icon == nint.Zero)
        {
            logger.LogWarning("Could not load the NoteKeeper tray icon. Win32 error: {Error}", Marshal.GetLastWin32Error());
        }

        return icon;
    }

    private void AddTrayIcon()
    {
        if (_windowHandle == nint.Zero)
        {
            return;
        }

        try
        {
            var data = CreateNotifyIconData();
            ShellNotifyIcon(NimDelete, ref data);

            if (!ShellNotifyIcon(NimAdd, ref data))
            {
                logger.LogWarning("Could not add the NoteKeeper icon to the system tray. Win32 error: {Error}", Marshal.GetLastWin32Error());
            }
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Could not initialize the NoteKeeper system tray icon.");
        }
    }

    private void RemoveTrayIcon()
    {
        if (_windowHandle == nint.Zero)
        {
            return;
        }

        try
        {
            var data = CreateNotifyIconData();
            ShellNotifyIcon(NimDelete, ref data);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Could not remove the NoteKeeper system tray icon.");
        }
    }

    private NotifyIconData CreateNotifyIconData()
    {
        return new NotifyIconData
        {
            cbSize = (uint)Marshal.SizeOf<NotifyIconData>(),
            hWnd = _windowHandle,
            uID = 1,
            uFlags = NifMessage | NifIcon | NifTip,
            uCallbackMessage = WmTrayIcon,
            hIcon = _iconHandle,
            szTip = "NoteKeeper"
        };
    }

    private delegate nint WndProc(nint windowHandle, uint message, nint wParam, nint lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WindowClass
    {
        public uint style;
        public WndProc lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public nint hInstance;
        public nint hIcon;
        public nint hCursor;
        public nint hbrBackground;
        public string? lpszMenuName;
        public string lpszClassName;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Message
    {
        public nint hwnd;
        public uint message;
        public nuint wParam;
        public nint lParam;
        public uint time;
        public Point point;
        public uint lPrivate;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyIconData
    {
        public uint cbSize;
        public nint hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public nint hIcon;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szTip;

        public uint dwState;
        public uint dwStateMask;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string szInfo;

        public uint uTimeoutOrVersion;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string szInfoTitle;

        public uint dwInfoFlags;
        public Guid guidItem;
        public nint hBalloonIcon;
    }

    [DllImport("kernel32.dll", EntryPoint = "GetModuleHandleW", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern nint GetModuleHandle(string? moduleName);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll", EntryPoint = "RegisterClassW", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    private static extern ushort RegisterClass(ref WindowClass windowClass);

    [DllImport("user32.dll", EntryPoint = "UnregisterClassW", CharSet = CharSet.Unicode, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterClass(string className, nint instance);

    [DllImport("user32.dll", EntryPoint = "CreateWindowExW", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    private static extern nint CreateWindowEx(
        uint extendedStyle,
        string className,
        string windowName,
        uint style,
        int x,
        int y,
        int width,
        int height,
        nint parentWindow,
        nint menu,
        nint instance,
        nint parameter);

    [DllImport("user32.dll")]
    private static extern bool DestroyWindow(nint windowHandle);

    [DllImport("user32.dll", EntryPoint = "DefWindowProcW", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern nint DefWindowProc(nint windowHandle, uint message, nint wParam, nint lParam);

    [DllImport("user32.dll")]
    private static extern int GetMessage(out Message message, nint windowHandle, uint minimumMessage, uint maximumMessage);

    [DllImport("user32.dll")]
    private static extern bool TranslateMessage(ref Message message);

    [DllImport("user32.dll")]
    private static extern nint DispatchMessage(ref Message message);

    [DllImport("user32.dll")]
    private static extern void PostQuitMessage(int exitCode);

    [DllImport("user32.dll")]
    private static extern bool PostMessage(nint windowHandle, uint message, nint wParam, nint lParam);

    [DllImport("user32.dll")]
    private static extern bool PostThreadMessage(uint threadId, uint message, nint wParam, nint lParam);

    [DllImport("user32.dll", EntryPoint = "RegisterWindowMessageW", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern uint RegisterWindowMessage(string message);

    [DllImport("user32.dll", EntryPoint = "LoadImageW", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    private static extern nint LoadImage(nint instance, string name, uint type, int width, int height, uint loadFlags);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(nint icon);

    [DllImport(
        "shell32.dll",
        EntryPoint = "Shell_NotifyIconW",
        CharSet = CharSet.Unicode,
        ExactSpelling = true,
        SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShellNotifyIcon(uint message, ref NotifyIconData data);

    [DllImport("user32.dll")]
    private static extern nint CreatePopupMenu();

    [DllImport("user32.dll", EntryPoint = "AppendMenuW", CharSet = CharSet.Unicode, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AppendMenu(nint menu, uint flags, uint itemId, string text);

    [DllImport("user32.dll")]
    private static extern bool DestroyMenu(nint menu);

    [DllImport("user32.dll")]
    private static extern uint TrackPopupMenu(
        nint menu,
        uint flags,
        int x,
        int y,
        int reserved,
        nint windowHandle,
        nint rectangle);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out Point point);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(nint windowHandle);
}
