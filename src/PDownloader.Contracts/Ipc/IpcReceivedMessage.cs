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

namespace PDownloader.Contracts.Ipc;

/// <summary>
/// Immutable received message with typed payload helpers.
/// </summary>
public sealed class IpcReceivedMessage
{
    private static readonly JsonSerializerOptions SerializerOptions = IpcJson.CreateSerializerOptions();

    public IpcReceivedMessage(IpcEnvelope envelope)
    {
        Envelope = envelope ?? throw new ArgumentNullException(nameof(envelope));
    }

    public IpcEnvelope Envelope { get; }
    public int Version => Envelope.Version;
    public string Type => Envelope.Type;
    public string RequestId => Envelope.RequestId;

    public bool Is<TPayload>(IpcMessageDefinition<TPayload> definition) =>
        definition is not null
        && string.Equals(Type, definition.Name, StringComparison.Ordinal);

    public TPayload GetPayload<TPayload>(IpcMessageDefinition<TPayload> definition)
    {
        if (!Is(definition))
        {
            throw new InvalidOperationException(
                $"Message type '{Type}' does not match '{definition.Name}'.");
        }

        TPayload? payload = Envelope.Payload.Deserialize<TPayload>(SerializerOptions);
        if (payload is null)
        {
            throw new JsonException($"Message '{Type}' contains a null payload.");
        }

        return payload;
    }

    public bool TryGetPayload<TPayload>(
        IpcMessageDefinition<TPayload> definition,
        out TPayload payload)
    {
        payload = default!;
        if (!Is(definition))
        {
            return false;
        }

        try
        {
            TPayload? parsed = Envelope.Payload.Deserialize<TPayload>(SerializerOptions);
            if (parsed is null)
            {
                return false;
            }

            payload = parsed;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
        catch (NotSupportedException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }
}
