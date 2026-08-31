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

namespace PDownloader.Downloads;

public sealed partial class DownloadManager
{
    public async ValueTask DisposeAsync()
    {
        TaskCompletionSource? owner = null;
        Task completion;
        Task[] commands = Array.Empty<Task>();
        lock (_sync)
        {
            if (_disposeTask is null)
            {
                owner = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                _disposeTask = owner.Task; // Closes command admission atomically.
                commands = _commands.ToArray();
            }

            completion = _disposeTask;
        }

        if (owner is not null)
        {
            try
            {
                // Cancel first: an admitted Pause/Cancel command may await a worker.
                // Await command tails even when one command has failed.
                try
                {
                    await Task.WhenAll(_shutdown.CancelAsync(), Task.WhenAll(commands)).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[DownloadManager] Draining commands: {ex.Message}");
                }

                DownloadSession[] sessions;
                lock (_sync)
                {
                    sessions = _sessions.Values.ToArray();
                }

                try
                {
                    await Task.WhenAll(sessions.Select(session => session.DisposeAsync().AsTask()))
                        .ConfigureAwait(false);
                }
                finally
                {
                    // No worker or hash waiter may still reference these resources.
                    _hashSemaphore.Dispose();
                    _shutdown.Dispose();
                    GC.SuppressFinalize(this);
                }

                owner.TrySetResult();
            }
            catch (Exception ex) { owner.TrySetException(ex); }
        }

        await completion.ConfigureAwait(false);
    }
}
