using System.Runtime.InteropServices;

namespace NoteKeeper.Services;

internal static class WindowsConsoleVisibility
{
    private const int SwHide = 0;

    public static void HideOwnedConsole()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var consoleWindow = GetConsoleWindow();
        if (consoleWindow == nint.Zero)
        {
            return;
        }

        var processIds = new uint[8];
        var processCount = GetConsoleProcessList(
            processIds,
            (uint)processIds.Length);

        if (processCount != 1)
        {
            return;
        }

        ShowWindow(consoleWindow, SwHide);
    }

    [DllImport("kernel32.dll")]
    private static extern nint GetConsoleWindow();

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint GetConsoleProcessList(
        uint[] processList,
        uint processCount);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(
        nint windowHandle,
        int command);
}
