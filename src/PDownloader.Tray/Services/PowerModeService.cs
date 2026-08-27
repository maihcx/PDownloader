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

using System.Diagnostics;
using System.Runtime.InteropServices;

namespace PDownloader.Tray.Services;

public class PowerModeService : IDisposable
{
    public enum PowerModeState
    {
        /// <summary>
        /// Full refresh rate, no throttling. App is in foreground and active.
        /// </summary>
        Normal,

        /// <summary>
        /// Reduced refresh rate, EcoQoS enabled. App is minimized or in background.
        /// </summary>
        Efficiency,

        /// <summary>
        /// Minimal refresh rate, EcoQoS + lower process priority.
        /// App has been idle/background for an extended period, or system is on battery saver.
        /// </summary>
        EfficiencyAdvanced
    }

    public delegate void PowerModeChangedEventHandler(PowerModeState oldMode, PowerModeState newMode);

    public event PowerModeChangedEventHandler? PowerModeChanged;

    public PowerModeState CurrentPowerModeState = PowerModeState.Normal;

    private readonly SemaphoreSlim optimizeLock = new(1, 1);

    private CancellationTokenSource? _optimizeDelayCts;

    private readonly object _syncRoot = new();

    private bool _disposed;

    public void SetPowerMode(PowerModeState mode)
    {
        if (CurrentPowerModeState == mode)
        {
            return;
        }

        PowerModeState oldMode = CurrentPowerModeState;
        CurrentPowerModeState = mode;

        var throttlingFlags = NativeMethods.PROCESS_POWER_THROTTLING_EXECUTION_SPEED | NativeMethods.PROCESS_POWER_THROTTLING_IGNORE_TIMER_RESOLUTION;

        var state = new NativeMethods.PROCESS_POWER_THROTTLING_STATE
        {
            Version = NativeMethods.PROCESS_POWER_THROTTLING_CURRENT_VERSION,
            ControlMask = throttlingFlags,
            StateMask = mode != PowerModeState.Normal ? throttlingFlags : 0
        };

        using var process = Process.GetCurrentProcess();
        process.PriorityClass = mode switch
        {
            PowerModeState.Normal => ProcessPriorityClass.Normal,
            PowerModeState.Efficiency => ProcessPriorityClass.BelowNormal,
            PowerModeState.EfficiencyAdvanced => ProcessPriorityClass.Idle,
            _ => ProcessPriorityClass.Normal
        };

        NativeMethods.SetProcessInformation(
            process.Handle,
            NativeMethods.PROCESS_INFORMATION_CLASS.ProcessPowerThrottling,
            ref state,
            (uint)Marshal.SizeOf(state));

        if (mode != PowerModeState.Normal)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            NativeMethods.EmptyWorkingSet(Process.GetCurrentProcess().Handle);
        }

        PowerModeChanged?.Invoke(oldMode, mode);
    }

    public async Task OptimizeAsync()
    {
        await optimizeLock.WaitAsync();

        try
        {
            await Task.Run(() =>
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();

                NativeMethods.EmptyWorkingSet(Process.GetCurrentProcess().Handle);
            });
        }
        finally
        {
            optimizeLock.Release();
        }
    }

    public async Task OptimizeAfterAsync(TimeSpan? delay = null)
    {
        delay ??= TimeSpan.FromSeconds(5);

        CancellationTokenSource cts;

        lock (_syncRoot)
        {
            _optimizeDelayCts?.Cancel();
            _optimizeDelayCts?.Dispose();

            _optimizeDelayCts = new CancellationTokenSource();
            cts = _optimizeDelayCts;
        }

        try
        {
            await Task.Delay(delay.Value, cts.Token);
            await OptimizeAsync();
        }
        catch (OperationCanceledException) { }
    }

    protected virtual void Dispose(bool disposing)
    {
        if (_disposed)
        {
            return;
        }

        if (disposing)
        {
            optimizeLock.Dispose();
            _optimizeDelayCts?.Dispose();

            GC.SuppressFinalize(this);
        }

        _disposed = true;
    }

    public void Dispose()
    {
        Dispose(true);
    }
}