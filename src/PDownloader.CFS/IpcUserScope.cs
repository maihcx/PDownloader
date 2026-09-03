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

using System.Security.Principal;

namespace PDownloader.CFS;

/// <summary>OS identity used to scope IPC endpoints; contains no application protocol rules.</summary>
public static class IpcUserScope
{
    public static string ScopeName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        string suffix = "-" + CurrentUserId;
        return name.EndsWith(suffix, StringComparison.Ordinal) ? name : name + suffix;
    }

    public static string CurrentUserId
    {
        get
        {
            using var identity = WindowsIdentity.GetCurrent();
            return identity.User?.Value
                ?? throw new InvalidOperationException("Cannot resolve the current Windows user for IPC.");
        }
    }
}
