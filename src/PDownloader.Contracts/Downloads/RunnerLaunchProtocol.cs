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

namespace PDownloader.Contracts.Downloads;

/// <summary>
/// Command-line contract used by Core when launching a Runner process.
/// </summary>
public static class RunnerLaunchProtocol
{
    // The Runner receives only its opaque session token on the command line.
    // URL, file path, thread count and all sensitive headers are transferred
    // through the authenticated local IPC session after startup.
    public const string TokenArgument = "--token";
}
