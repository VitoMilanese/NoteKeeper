using System.Diagnostics;
using System.Runtime.Versioning;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;

namespace NoteKeeper.Services;

public sealed class WindowsTrayIconService(
    IHostApplicationLifetime applicationLifetime,
    IHostEnvironment environment,
    IServer server,
    ILogger<WindowsTrayIconService> logger) : IHostedService, IDisposable
{
    private readonly object _sync = new();

    private CancellationTokenRegistration _startedRegistration;
    private RegisteredWaitHandle? _exitRegistration;
    private EventWaitHandle? _exitEvent;
    private Process? _trayProcess;
    private bool _disposed;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
        {
            return Task.CompletedTask;
        }

        if (environment.IsDevelopment())
        {
            logger.LogInformation(
                "Skipping the NoteKeeper tray helper in the Development environment. Run NoteKeeper.exe directly to test tray integration.");
            return Task.CompletedTask;
        }

        _startedRegistration = applicationLifetime.ApplicationStarted.Register(TryStartTrayProcess);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        if (OperatingSystem.IsWindows())
        {
            StopTrayProcess();
        }

        return Task.CompletedTask;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _startedRegistration.Dispose();

        if (OperatingSystem.IsWindows())
        {
            StopTrayProcess();
        }
    }

    [SupportedOSPlatform("windows")]
    private void TryStartTrayProcess()
    {
        try
        {
            StartTrayProcess();
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Could not start the NoteKeeper system tray helper.");
        }
    }

    [SupportedOSPlatform("windows")]
    private void StartTrayProcess()
    {
        lock (_sync)
        {
            if (_disposed || _trayProcess is { HasExited: false })
            {
                return;
            }

            var trayExecutable = Path.Combine(
                AppContext.BaseDirectory,
                "Tray",
                "NoteKeeper.Tray.exe");

            if (!File.Exists(trayExecutable))
            {
                logger.LogWarning(
                    "The NoteKeeper tray helper was not found at {Path}. The web application will continue without a tray icon.",
                    trayExecutable);
                return;
            }

            var eventName = $"Local\\NoteKeeper.Exit.{Environment.ProcessId}.{Guid.NewGuid():N}";
            var exitEvent = new EventWaitHandle(false, EventResetMode.AutoReset, eventName);
            var homeUrl = ResolveHomeUrl();

            var startInfo = new ProcessStartInfo
            {
                FileName = trayExecutable,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = AppContext.BaseDirectory
            };
            startInfo.ArgumentList.Add("--parent-pid");
            startInfo.ArgumentList.Add(Environment.ProcessId.ToString());
            startInfo.ArgumentList.Add("--url");
            startInfo.ArgumentList.Add(homeUrl);
            startInfo.ArgumentList.Add("--exit-event");
            startInfo.ArgumentList.Add(eventName);

            var trayProcess = Process.Start(startInfo);
            if (trayProcess is null)
            {
                exitEvent.Dispose();
                logger.LogWarning("The NoteKeeper tray helper process could not be started.");
                return;
            }

            _exitEvent = exitEvent;
            _trayProcess = trayProcess;
            _exitRegistration = ThreadPool.RegisterWaitForSingleObject(
                exitEvent,
                static (state, timedOut) =>
                {
                    if (!timedOut && state is IHostApplicationLifetime lifetime)
                    {
                        lifetime.StopApplication();
                    }
                },
                applicationLifetime,
                Timeout.Infinite,
                executeOnlyOnce: true);

            logger.LogInformation(
                "Started the NoteKeeper system tray helper process {ProcessId}.",
                trayProcess.Id);
        }
    }

    [SupportedOSPlatform("windows")]
    private void StopTrayProcess()
    {
        lock (_sync)
        {
            _exitRegistration?.Unregister(null);
            _exitRegistration = null;

            _exitEvent?.Dispose();
            _exitEvent = null;

            if (_trayProcess is not null)
            {
                try
                {
                    if (!_trayProcess.HasExited)
                    {
                        _trayProcess.Kill(entireProcessTree: true);
                        _trayProcess.WaitForExit(2000);
                    }
                }
                catch (Exception exception)
                {
                    logger.LogDebug(exception, "Could not stop the NoteKeeper tray helper process cleanly.");
                }
                finally
                {
                    _trayProcess.Dispose();
                    _trayProcess = null;
                }
            }
        }
    }

    private string ResolveHomeUrl()
    {
        var addresses = server.Features.Get<IServerAddressesFeature>()?.Addresses;
        var address = addresses?
            .Where(value =>
                value.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                value.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(value =>
                value.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            .FirstOrDefault();

        if (string.IsNullOrWhiteSpace(address) ||
            !Uri.TryCreate(address, UriKind.Absolute, out var uri))
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
}
