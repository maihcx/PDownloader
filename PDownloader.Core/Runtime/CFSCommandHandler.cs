namespace PDownloader.Core.Runtime
{
    public static class CFSCommandHandler
    {
        public static void Handle(string name, string value)
        {
            switch (name)
            {
                case "main-event":
                    AppRuntime.cfsTray?.Send(name, value);
                    break;

                case "tray-event":
                case "state":
                    HandleMainEvent(name, value);
                    break;

                case "core-svc-state":
                    HandleCoreState(value);
                    break;
            }
        }

        private static void HandleMainEvent(string name, string value)
        {
            if (!AppRuntime.cfsMain!.IsAppStarted())
                AppRuntime.cfsMain.StartApp();

            AppRuntime.cfsMain.Send(name, value);
        }

        private static void HandleCoreState(string value)
        {
            if (value == "shutdown")
            {
                if (AppRuntime.cfsMain?.IsAppStarted() == true)
                    AppRuntime.cfsMain.Send("state", value);

                AppRuntime.bootstrap?.Shutdown();
            }
        }
    }
}
