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
}
