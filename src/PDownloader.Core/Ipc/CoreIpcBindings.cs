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

using PDownloader.Core.Services.DownloadServices;

namespace PDownloader.Core.Ipc;

/// <summary>
/// Composition-only IPC adapter. Each process type gets only the messages it is
/// allowed to send; handlers immediately delegate to application services.
/// </summary>
public sealed class CoreIpcBindings : IDisposable
{
    private readonly AppEventRelay _appEvents;
    private readonly CoreLifecycleService _lifecycle;
    private readonly MainAppGateway _mainGateway;
    private readonly DownloadConfigService _downloadConfig;
    private readonly DownloadCommandService _downloadCommands;
    private readonly DownloadLaunchService _downloadLauncher;
    private readonly CoreUpdateCoordinator _updates;
    private readonly RunnerSessionManager _runnerSessions;
    private int _disposed;

    public CoreIpcBindings(
        AppEventRelay appEvents,
        CoreLifecycleService lifecycle,
        MainAppGateway mainGateway,
        DownloadConfigService downloadConfig,
        DownloadCommandService downloadCommands,
        DownloadLaunchService downloadLauncher,
        CoreUpdateCoordinator updates,
        RunnerSessionManager runnerSessions)
    {
        _appEvents = appEvents;
        _lifecycle = lifecycle;
        _mainGateway = mainGateway;
        _downloadConfig = downloadConfig;
        _downloadCommands = downloadCommands;
        _downloadLauncher = downloadLauncher;
        _updates = updates;
        _runnerSessions = runnerSessions;
        _runnerSessions.SessionStarted += BindRunner;
    }

    public void BindMain(ConfluxService main)
    {
        main.RegisterMessageHandler(
            AppProtocol.MainEvent,
            _appEvents.RelayMainEvent);
        main.RegisterMessageHandler(
            AppProtocol.CoreServiceState,
            _lifecycle.HandleCoreStateAsync);
        main.RegisterMessageHandler(
            AppProtocol.MainReady,
            _mainGateway.NotifyReady);
        main.RegisterMessageHandler(
            DownloadSettingsProtocol.Reload,
            _downloadConfig.Reload);
        main.RegisterMessageHandler(
            UpdateProtocol.Command,
            _updates.HandleCommand);
        main.RegisterMessageHandler(
            DownloadProtocol.DownloadByLink,
            (request, cancellationToken) =>
                _downloadLauncher.LaunchFromUrlAsync(
                    request,
                    cancellationToken));

        BindMainDownloadControls(main);

        main.RegisterRequestHandler(
            DownloadProtocol.GetList,
            _downloadCommands.GetList);
        main.RegisterRequestHandler(
            UpdateProtocol.GetState,
            _updates.GetStateSnapshot);
    }

    public void BindTray(ConfluxService tray)
    {
        tray.RegisterMessageHandler(
            AppProtocol.TrayEvent,
            _appEvents.ForwardTrayEvent);
        tray.RegisterMessageHandler(
            AppProtocol.State,
            _appEvents.ForwardMainState);
        tray.RegisterMessageHandler(
            AppProtocol.CoreServiceState,
            _lifecycle.HandleCoreStateAsync);
        tray.RegisterMessageHandler(
            UpdateProtocol.Command,
            _updates.HandleCommand);

        tray.RegisterRequestHandler(
            UpdateProtocol.GetState,
            _updates.GetStateSnapshot);
    }

    private void BindRunner(RunnerSession session)
    {
        ConfluxService runner = session.Channel;

        runner.RegisterRequestHandler(
            DownloadProtocol.RunnerGetSession,
            () => session.Context.ToView());

        runner.RegisterMessageHandler(
            DownloadProtocol.RunnerStartDownload,
            request => _downloadLauncher.StartFromRunner(session, request));

        BindRunnerDownloadControls(runner, session);

        runner.RegisterMessageHandler(
            DownloadProtocol.RunnerCancelExperience,
            () => _ = _runnerSessions.CloseAsync(session.Id));
        runner.RegisterMessageHandler(
            DownloadProtocol.RunnerUiClosed,
            () => _ = _runnerSessions.CloseAsync(session.Id));
    }

    private void BindMainDownloadControls(ConfluxService main)
    {
        main.RegisterMessageHandler(
            DownloadProtocol.RunnerPause,
            request => _downloadCommands.Pause(request.DownloadId));
        main.RegisterMessageHandler(
            DownloadProtocol.RunnerResume,
            request => _downloadCommands.Resume(request.DownloadId));
        main.RegisterMessageHandler(
            DownloadProtocol.RunnerRetry,
            request => _downloadCommands.Retry(request.DownloadId));
        main.RegisterMessageHandler(
            DownloadProtocol.RunnerCancel,
            request => _downloadCommands.Cancel(request.DownloadId));
        main.RegisterMessageHandler(
            DownloadProtocol.RunnerClear,
            _downloadCommands.Clear);
        main.RegisterMessageHandler(
            DownloadProtocol.RunnerPauseAll,
            _downloadCommands.PauseAll);
        main.RegisterMessageHandler(
            DownloadProtocol.RunnerResumeAll,
            _downloadCommands.ResumeAll);
        main.RegisterMessageHandler(
            DownloadProtocol.RunnerRetryAll,
            _downloadCommands.RetryAll);
    }

    private void BindRunnerDownloadControls(
        ConfluxService runner,
        RunnerSession session)
    {
        // Runner identity comes from its private pipe/session, not from a payload
        // supplied by the Runner process. This prevents one Runner from
        // controlling another download by forging DownloadIdRequest.DownloadId.
        runner.RegisterMessageHandler(
            DownloadProtocol.RunnerPause,
            _ => _downloadCommands.Pause(session.Id));
        runner.RegisterMessageHandler(
            DownloadProtocol.RunnerResume,
            _ => _downloadCommands.Resume(session.Id));
        runner.RegisterMessageHandler(
            DownloadProtocol.RunnerRetry,
            _ => _downloadCommands.Retry(session.Id));
        runner.RegisterMessageHandler(
            DownloadProtocol.RunnerCancel,
            _ => _downloadCommands.Cancel(session.Id));
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _runnerSessions.SessionStarted -= BindRunner;
        GC.SuppressFinalize(this);
    }
}
