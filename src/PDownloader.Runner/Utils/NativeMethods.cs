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

namespace PDownloader.Runner.Utils;

internal static class NativeMethods
{
    [DllImport("psapi.dll")]
    public static extern bool EmptyWorkingSet(IntPtr hProcess);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool SetProcessInformation(
        IntPtr hProcess,
        PROCESS_INFORMATION_CLASS processInformationClass,
        ref PROCESS_POWER_THROTTLING_STATE processInformation,
        uint processInformationSize);

    public enum PROCESS_INFORMATION_CLASS
    {
        ProcessPowerThrottling = 4
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PROCESS_POWER_THROTTLING_STATE
    {
        public uint Version;
        public uint ControlMask;
        public uint StateMask;
    }

    public const uint PROCESS_POWER_THROTTLING_CURRENT_VERSION = 1;
    public const uint PROCESS_POWER_THROTTLING_EXECUTION_SPEED = 0x1;
    public const uint PROCESS_POWER_THROTTLING_IGNORE_TIMER_RESOLUTION = 0x4;

    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    public static extern short GetAsyncKeyState(int virtualKey);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern uint SendInput(uint count, [In] INPUT[] inputs, int size);

    // Keep the complete native union: a keyboard-only union has the wrong
    // sizeof(INPUT), notably on both Windows x64 and ARM64 (40 bytes).
    [StructLayout(LayoutKind.Sequential)]
    public struct INPUT
    {
        public uint type;
        public INPUTUNION data;
    }

    [StructLayout(LayoutKind.Explicit)]
    public struct INPUTUNION
    {
        [FieldOffset(0)] public MOUSEINPUT mouse;
        [FieldOffset(0)] public KEYBDINPUT keyboard;
        [FieldOffset(0)] public HARDWAREINPUT hardware;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public UIntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public UIntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct HARDWAREINPUT
    {
        public uint uMsg;
        public ushort wParamL;
        public ushort wParamH;
    }

    public static INPUT CreateAltInput(bool keyUp) => new()
    {
        type = 1, // INPUT_KEYBOARD
        data = new INPUTUNION
        {
            keyboard = new KEYBDINPUT
            {
                wVk = 0x12, // VK_MENU (Alt)
                dwFlags = keyUp ? 0x0002u : 0u // KEYEVENTF_KEYUP
            }
        }
    };

    [DllImport("user32.dll")]
    public static extern bool FlashWindowEx(ref FLASHWINFO flashInfo);

    [StructLayout(LayoutKind.Sequential)]
    public struct FLASHWINFO
    {
        public uint cbSize;
        public IntPtr hwnd;
        public uint dwFlags;
        public uint uCount;
        public uint dwTimeout;
    }

    public static FLASHWINFO CreateFlashInfo(IntPtr handle, uint flags) => new()
    {
        cbSize = (uint)Marshal.SizeOf<FLASHWINFO>(),
        hwnd = handle,
        dwFlags = flags,
        uCount = uint.MaxValue,
        dwTimeout = 0
    };

    public const int SW_RESTORE = 9;
    public const uint FLASHW_STOP = 0;
    public const uint FLASHW_ALL = 0x00000003;
    public const uint FLASHW_TIMERNOFG = 0x0000000C;
}
