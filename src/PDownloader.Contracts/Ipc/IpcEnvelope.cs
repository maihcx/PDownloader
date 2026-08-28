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

using System.Text.Json;

namespace PDownloader.Contracts.Ipc;

/// <summary>
/// Versioned wire envelope. Payload remains JSON until a receiver asks for the
/// payload through a typed <see cref="IpcMessageDefinition{TPayload}"/>.
/// </summary>
public sealed class IpcEnvelope
{
    public int Version { get; init; } = IpcProtocol.CurrentVersion;
    public IpcEnvelopeKind Kind { get; init; } = IpcEnvelopeKind.Message;
    public string Type { get; init; } = string.Empty;
    public string RequestId { get; init; } = string.Empty;
    public JsonElement Payload { get; init; }
}

public enum IpcEnvelopeKind
{
    Message,
    Request
}

/// <summary>
/// Delivery acknowledgement returned for one-way messages.
/// RequestId correlates the acknowledgement to its originating envelope.
/// </summary>
public sealed class IpcAcknowledgement
{
    public int Version { get; init; } = IpcProtocol.CurrentVersion;
    public string RequestId { get; init; } = string.Empty;
    public bool Success { get; init; }
    public string? Error { get; init; }
}

/// <summary>
/// Application response for a correlated IPC request.
/// </summary>
public sealed class IpcResponseEnvelope
{
    public int Version { get; init; } = IpcProtocol.CurrentVersion;
    public string Type { get; init; } = string.Empty;
    public string RequestId { get; init; } = string.Empty;
    public bool Success { get; init; }
    public JsonElement Payload { get; init; }
    public string? Error { get; init; }
}
