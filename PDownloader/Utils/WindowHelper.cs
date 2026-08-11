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

namespace PDownloader.Utils;

public static class WindowHelper
{
    public static void BringToFront(Window window)
    {
        if (window == null)
        {
            return;
        }

        var handle = new WindowInteropHelper(window).Handle;

        NativeMethods.ShowWindow(handle, NativeMethods.SW_RESTORE);

        IntPtr foreground = NativeMethods.GetForegroundWindow();
        uint curThread = NativeMethods.GetCurrentThreadId();

        NativeMethods.keybd_event(NativeMethods.VK_MENU, 0, 0, UIntPtr.Zero);
        NativeMethods.keybd_event(NativeMethods.VK_MENU, 0, NativeMethods.KEYEVENTF_KEYUP, UIntPtr.Zero);

        if (foreground != IntPtr.Zero)
        {
            uint fgThread = NativeMethods.GetWindowThreadProcessId(foreground, IntPtr.Zero);
            NativeMethods.AttachThreadInput(fgThread, curThread, true);
            NativeMethods.SetForegroundWindow(handle);
            NativeMethods.AttachThreadInput(fgThread, curThread, false);
        }
        else
        {
            NativeMethods.SetForegroundWindow(handle);
        }

        window.Activate();
        window.Focus();
    }

    public static void FocusMainWindow()
    {
        if (System.Windows.Application.Current.MainWindow is MainWindow mw)
        {
            if (!mw.IsVisible)
            {
                mw.ShowWithEffect();
            }
            else
            {
                if (mw.WindowState == WindowState.Minimized)
                {
                    mw.WindowState = WindowState.Normal;
                }

                mw.Activate();
            }

            BringToFront(mw);
        }
    }
}
