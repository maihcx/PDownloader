// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program. If not, see <https://www.gnu.org/licenses/>.
//
// Copyright (C) Song Mai Software.

using System.Windows.Interop;

namespace PDownloader.Runner.Utils;

public static class WindowHelper
{
    public static ApplicationThemeManagerService? ThemeManagerService;

    public static Window? MainWindow;

    public static void BringToFront(Window window)
    {
        if (window == null)
        {
            return;
        }

        if (!window.Dispatcher.CheckAccess())
        {
            window.Dispatcher.Invoke(() => BringToFront(window));
            return;
        }

        // Never resurrect a closed window or activate a disabled modal owner.
        if (!window.IsVisible || !window.IsEnabled)
        {
            return;
        }

        IntPtr handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        if (window.WindowState == WindowState.Minimized)
        {
            NativeMethods.ShowWindow(handle, NativeMethods.SW_RESTORE);
        }

        if (TryActivate(handle))
        {
            CompleteActivation(window, handle);
            return;
        }

        // Compatibility fallback for the existing HTTP-only browser extension.
        // HTTP does not grant foreground permission. Restore the old Alt-based
        // attempt, but use checked SendInput and do not join input queues.
        // This is best effort, not an AllowSetForegroundWindow handshake.
        if (TrySendActivationKey() && TryActivate(handle))
        {
            CompleteActivation(window, handle);
            return;
        }

        Debug.WriteLine("[Runner focus] Foreground activation was denied.");
        FlashUntilForeground(handle);
    }

    private static bool TryActivate(IntPtr handle)
    {
        if (NativeMethods.GetForegroundWindow() == handle)
        {
            return true;
        }

        bool accepted = NativeMethods.SetForegroundWindow(handle);
        bool foreground = NativeMethods.GetForegroundWindow() == handle;
        Debug.WriteLine($"[Runner focus] SetForegroundWindow={accepted}, foreground={foreground}.");
        return foreground;
    }

    private static void CompleteActivation(Window window, IntPtr handle)
    {
        StopFlashing(handle);
        // WPF restores the active control itself; focusing the Window here can
        // take keyboard focus away from the filename/path editor.
        window.Activate();
    }

    private static bool TrySendActivationKey()
    {
        // Do not combine synthetic Alt with a physical key or mouse button.
        // Check the high bit (currently down), not the historical pressed bit.
        for (int key = 1; key < 256; key++)
        {
            if ((NativeMethods.GetAsyncKeyState(key) & 0x8000) != 0)
            {
                return false;
            }
        }

        NativeMethods.INPUT keyUp = NativeMethods.CreateAltInput(keyUp: true);
        NativeMethods.INPUT[] inputs =
        [
            NativeMethods.CreateAltInput(keyUp: false),
            keyUp
        ];
        int inputSize = Marshal.SizeOf<NativeMethods.INPUT>();
        uint sent = NativeMethods.SendInput((uint)inputs.Length, inputs, inputSize);
        if (sent == (uint)inputs.Length)
        {
            return true;
        }

        int error = Marshal.GetLastWin32Error();
        if (sent == 1)
        {
            // A partial insertion must not leave our synthetic Alt down.
            uint released = NativeMethods.SendInput(1, [keyUp], inputSize);
            if (released != 1)
            {
                Debug.WriteLine("[Runner focus] Synthetic Alt release was rejected.");
            }
        }

        // UIPI/secure desktop can reject this attempt; do not retry in a loop.
        Debug.WriteLine($"[Runner focus] SendInput inserted {sent}/2 events, error={error}.");
        return false;
    }

    public static void StopFlashing(Window window)
    {
        if (window is null)
        {
            return;
        }

        IntPtr handle = new WindowInteropHelper(window).Handle;
        if (handle != IntPtr.Zero)
        {
            StopFlashing(handle);
        }
    }

    private static void FlashUntilForeground(IntPtr handle)
    {
        var info = NativeMethods.CreateFlashInfo(
            handle,
            NativeMethods.FLASHW_ALL | NativeMethods.FLASHW_TIMERNOFG);
        NativeMethods.FlashWindowEx(ref info);
    }

    private static void StopFlashing(IntPtr handle)
    {
        var info = NativeMethods.CreateFlashInfo(
            handle,
            NativeMethods.FLASHW_STOP);
        NativeMethods.FlashWindowEx(ref info);
    }
}
