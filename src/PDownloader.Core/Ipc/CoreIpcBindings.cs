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
    private ConfluxService? _main;
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
        _downloadConfig.Changed += PublishDownloadSettings;
        _runnerSessions.SessionStarted += BindRunner;
        _runnerSessions.SessionReady += _downloadCommands.PublishRunnerSnapshot;
    }

    public void BindMain(ConfluxService main)
    {
        _main = main;
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

    private void PublishDownloadSettings(DownloadSettingsDto settings)
    {
        try
        {
            _main?.Send(DownloadSettingsProtocol.Changed, settings);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Settings] Could not publish download settings: {ex.Message}");
        }
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
            (request, token) => _downloadLauncher.StartFromRunnerAsync(session, request, token));

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
            (request, token) => _downloadCommands.PauseAsync(request.DownloadId, token));
        main.RegisterMessageHandler(
            DownloadProtocol.RunnerResume,
            (request, token) => _downloadCommands.ResumeAsync(request.DownloadId, token));
        main.RegisterMessageHandler(
            DownloadProtocol.RunnerRetry,
            (request, token) => _downloadCommands.RetryAsync(request.DownloadId, token));
        main.RegisterMessageHandler(
            DownloadProtocol.RunnerCancel,
            (request, token) => _downloadCommands.CancelAsync(request.DownloadId, token));
        main.RegisterMessageHandler(
            DownloadProtocol.RunnerClear,
            (scope, token) => _downloadCommands.ClearAsync(scope, token));
        main.RegisterMessageHandler(
            DownloadProtocol.RunnerPauseAll,
            (_, token) => _downloadCommands.PauseAllAsync(token));
        main.RegisterMessageHandler(
            DownloadProtocol.RunnerResumeAll,
            (_, token) => _downloadCommands.ResumeAllAsync(token));
        main.RegisterMessageHandler(
            DownloadProtocol.RunnerRetryAll,
            (_, token) => _downloadCommands.RetryAllAsync(token));
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
            (_, token) => _downloadCommands.PauseAsync(session.Id, token));
        runner.RegisterMessageHandler(
            DownloadProtocol.RunnerResume,
            (_, token) => _downloadCommands.ResumeAsync(session.Id, token));
        runner.RegisterMessageHandler(
            DownloadProtocol.RunnerRetry,
            (_, token) => _downloadCommands.RetryAsync(session.Id, token));
        runner.RegisterMessageHandler(
            DownloadProtocol.RunnerCancel,
            (_, token) => _downloadCommands.CancelAsync(session.Id, token));
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _runnerSessions.SessionStarted -= BindRunner;
        _runnerSessions.SessionReady -= _downloadCommands.PublishRunnerSnapshot;
        _downloadConfig.Changed -= PublishDownloadSettings;
        _main = null;
        GC.SuppressFinalize(this);
    }
}
