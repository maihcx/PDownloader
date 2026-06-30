namespace PDownloader.Services
{
    public class PowerModeService
    {
        [DllImport("psapi.dll")]
        static extern bool EmptyWorkingSet(IntPtr hProcess);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetProcessInformation(
            IntPtr hProcess,
            PROCESS_INFORMATION_CLASS processInformationClass,
            ref PROCESS_POWER_THROTTLING_STATE processInformation,
            uint processInformationSize);

        private enum PROCESS_INFORMATION_CLASS
        {
            ProcessPowerThrottling = 4
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct PROCESS_POWER_THROTTLING_STATE
        {
            public uint Version;
            public uint ControlMask;
            public uint StateMask;
        }

        private const uint PROCESS_POWER_THROTTLING_CURRENT_VERSION = 1;
        private const uint PROCESS_POWER_THROTTLING_EXECUTION_SPEED = 0x1;
        private const uint PROCESS_POWER_THROTTLING_IGNORE_TIMER_RESOLUTION = 0x4;

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

        private readonly SemaphoreSlim _lock = new(1, 1);

        public void SetPowerMode(PowerModeState mode)
        {
            if (CurrentPowerModeState == mode)
            {
                return;
            }

            var oldMode = CurrentPowerModeState;
            CurrentPowerModeState = mode;

            var throttlingFlags = PROCESS_POWER_THROTTLING_EXECUTION_SPEED | PROCESS_POWER_THROTTLING_IGNORE_TIMER_RESOLUTION;

            var state = new PROCESS_POWER_THROTTLING_STATE
            {
                Version = PROCESS_POWER_THROTTLING_CURRENT_VERSION,
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

            SetProcessInformation(
                process.Handle,
                PROCESS_INFORMATION_CLASS.ProcessPowerThrottling,
                ref state,
                (uint)Marshal.SizeOf(state));

            if (mode != PowerModeState.Normal)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();

                EmptyWorkingSet(Process.GetCurrentProcess().Handle);
            }

            PowerModeChanged?.Invoke(oldMode, mode);
        }

        public async Task OptimizeAsync()
        {
            await _lock.WaitAsync();

            try
            {
                await Task.Run(() =>
                {
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                    GC.Collect();

                    EmptyWorkingSet(Process.GetCurrentProcess().Handle);
                });
            }
            finally
            {
                _lock.Release();
            }
        }

        public async Task OptimizeAfterAsync(TimeSpan? delay = null)
        {
            if (!delay.HasValue)
            {
                delay = TimeSpan.FromSeconds(5);
            }
            await Task.Delay((int)delay.Value.TotalMilliseconds);

            await OptimizeAsync();
        }
    }
}