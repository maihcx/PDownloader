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

using PDownloader.Contracts.Ipc;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;

namespace PDownloader.CFS;

public sealed partial class ConfluxService : IDisposable, IAsyncDisposable
{
    private static readonly JsonSerializerOptions SerializerOptions = IpcJson.CreateSerializerOptions();

    public delegate void MessageReceive(IpcReceivedMessage message);
    public event MessageReceive? OnMessageReceived;

    public string ProcessPackage { get; set; } = string.Empty;
    public string ProcessName { get; set; } = string.Empty;
    public string SendPipeName { get; private set; } = string.Empty;
    public string ReceivePipeName { get; private set; } = string.Empty;
    public bool CanMultiple { get; set; }
    private readonly object _processSync = new();
    private Process? _currProcess;

    public bool CreateNoWindow;

    /// <summary>Maximum serialized envelope size, excluding the 4-byte frame header.</summary>
    public int MaxMessageBytes { get; set; } = 1024 * 1024;

    private CancellationTokenSource? _cts;
    private Task? _serviceTask;
    private readonly object _serviceSync = new();
    private Task? _stopTask;
    public int MaxConcurrentConnections { get; set; } = 8;
    public TimeSpan FrameTimeout { get; set; } = TimeSpan.FromSeconds(5);
    private readonly SemaphoreSlim _sendGate = new(1, 1);
    private readonly ConcurrentDictionary<
        string,
        Func<JsonElement, CancellationToken, Task>> _messageHandlers =
        new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<
        string,
        Func<JsonElement, CancellationToken, Task<JsonElement>>> _requestHandlers =
        new(StringComparer.Ordinal);
    private volatile bool _disposed;

    public void Register(
        string processPackage,
        string sendPipeName,
        string receivePipeName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(processPackage);
        ArgumentException.ThrowIfNullOrWhiteSpace(sendPipeName);
        ArgumentException.ThrowIfNullOrWhiteSpace(receivePipeName);

        ProcessPackage = processPackage;
        ProcessName = Path.GetFileNameWithoutExtension(processPackage);
        // Every endpoint (including private Runner pipes) is scoped to its user.
        SendPipeName = IpcUserScope.ScopeName(sendPipeName);
        ReceivePipeName = IpcUserScope.ScopeName(receivePipeName);
    }

    public void RegisterMessageHandler(
        IpcMessageDefinition<IpcNoPayload> definition,
        Action handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        RegisterMessageHandler(
            definition,
            _ => handler());
    }

    public void RegisterMessageHandler<TPayload>(
        IpcMessageDefinition<TPayload> definition,
        Action<TPayload> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        RegisterMessageHandler(
            definition,
            (payload, _) =>
            {
                handler(payload);
                return Task.CompletedTask;
            });
    }

    public void RegisterMessageHandler<TPayload>(
        IpcMessageDefinition<TPayload> definition,
        Func<TPayload, CancellationToken, Task> handler)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(handler);

        _messageHandlers[definition.Name] = async (payload, cancellationToken) =>
        {
            TPayload? messagePayload = payload.Deserialize<TPayload>(SerializerOptions);
            if (messagePayload is null)
            {
                throw new JsonException(
                    $"Message '{definition.Name}' contains a null payload.");
            }

            await handler(messagePayload, cancellationToken).ConfigureAwait(false);
        };
    }

    public void RegisterRequestHandler<TResponse>(
        IpcRequestDefinition<IpcNoPayload, TResponse> definition,
        Func<TResponse> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        RegisterRequestHandler(
            definition,
            _ => handler());
    }

    public void RegisterRequestHandler<TRequest, TResponse>(
        IpcRequestDefinition<TRequest, TResponse> definition,
        Func<TRequest, TResponse> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        RegisterRequestHandler(
            definition,
            (request, _) => Task.FromResult(handler(request)));
    }

    public void RegisterRequestHandler<TRequest, TResponse>(
        IpcRequestDefinition<TRequest, TResponse> definition,
        Func<TRequest, CancellationToken, Task<TResponse>> handler)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(handler);

        _requestHandlers[definition.Name] = async (payload, cancellationToken) =>
        {
            TRequest? request = payload.Deserialize<TRequest>(SerializerOptions);
            if (request is null)
            {
                throw new JsonException(
                    $"Request '{definition.Name}' contains a null payload.");
            }

            TResponse response = await handler(request, cancellationToken)
                .ConfigureAwait(false);
            return JsonSerializer.SerializeToElement(response, SerializerOptions);
        };
    }

    private static PipeSecurity CreateRestrictedPipeSecurity()
    {
        var security = new PipeSecurity();
        using var identity = WindowsIdentity.GetCurrent();
        SecurityIdentifier? currentUser = identity.User;

        if (currentUser != null)
        {
            security.AddAccessRule(new PipeAccessRule(
                currentUser,
                PipeAccessRights.ReadWrite | PipeAccessRights.CreateNewInstance,
                AccessControlType.Allow));
        }

        var adminsSid = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
        security.AddAccessRule(new PipeAccessRule(
            adminsSid,
            PipeAccessRights.ReadWrite,
            AccessControlType.Allow));
        return security;
    }

    private async Task HandleServerConnectionAsync(
        NamedPipeServerStream server,
        CancellationToken token)
    {
        IpcEnvelope envelope;
        try
        {
            using var frameDeadline = CancellationTokenSource.CreateLinkedTokenSource(token);
            frameDeadline.CancelAfter(FrameTimeout);
            byte[] payload = await ReadFrameAsync(server, frameDeadline.Token).ConfigureAwait(false);
            envelope = JsonSerializer.Deserialize<IpcEnvelope>(payload, SerializerOptions)
                ?? throw new JsonException("IPC envelope is null.");
        }
        catch (Exception ex) when (ex is JsonException or InvalidDataException or NotSupportedException)
        {
            Debug.WriteLine($"[CFS] Rejected malformed message: {ex.Message}");
            return;
        }

        try
        {
            ValidateEnvelope(envelope);
        }
        catch (InvalidDataException ex)
        {
            Debug.WriteLine($"[CFS] Rejected message: {ex.Message}");
            if (!string.IsNullOrWhiteSpace(envelope.RequestId))
            {
                try
                {
                    if (envelope.Kind == IpcEnvelopeKind.Request)
                    {
                        await WriteResponseAsync(
                            server,
                            new IpcResponseEnvelope
                            {
                                Type = envelope.Type,
                                RequestId = envelope.RequestId,
                                Success = false,
                                Payload = JsonSerializer.SerializeToElement(
                                    new IpcNoPayload(),
                                    SerializerOptions),
                                Error = ex.Message
                            },
                            token).ConfigureAwait(false);
                    }
                    else
                    {
                        await WriteAcknowledgementAsync(
                            server,
                            new IpcAcknowledgement
                            {
                                RequestId = envelope.RequestId,
                                Success = false,
                                Error = ex.Message
                            },
                            token).ConfigureAwait(false);
                    }
                }
                catch
                {
                }
            }

            return;
        }

        var message = new IpcReceivedMessage(envelope);

        if (envelope.Kind == IpcEnvelopeKind.Request && envelope.Type == IpcHealthProtocol.Get.Name)
        {
            await WriteResponseAsync(server, new IpcResponseEnvelope
            {
                Type = envelope.Type,
                RequestId = envelope.RequestId,
                Success = true,
                Payload = JsonSerializer.SerializeToElement(GetLocalHealth(), SerializerOptions)
            }, token).ConfigureAwait(false);
            return;
        }

        if (!_ready)
        {
            if (envelope.Kind == IpcEnvelopeKind.Request)
                await WriteResponseAsync(server, new IpcResponseEnvelope
                {
                    Type = envelope.Type, RequestId = envelope.RequestId, Success = false,
                    Payload = JsonSerializer.SerializeToElement(new IpcNoPayload(), SerializerOptions),
                    Error = "Endpoint is not ready."
                }, token).ConfigureAwait(false);
            else
                await WriteAcknowledgementAsync(server, new IpcAcknowledgement
                {
                    RequestId = envelope.RequestId, Success = false, Error = "Endpoint is not ready."
                }, token).ConfigureAwait(false);
            return;
        }

        if (envelope.Kind == IpcEnvelopeKind.Request)
        {
            var completed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            await QueueDispatchAsync(async ct =>
            {
                try
                {
                    await HandleRequestAsync(server, envelope, ct).ConfigureAwait(false);
                    completed.TrySetResult(true);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[CFS] Request connection ended: {ex.Message}");
                    completed.TrySetResult(false);
                }
            }, token).ConfigureAwait(false);
            await completed.Task.WaitAsync(token).ConfigureAwait(false);
            return;
        }

        if (!_messageHandlers.ContainsKey(envelope.Type) && OnMessageReceived is null)
        {
            await WriteAcknowledgementAsync(server, new IpcAcknowledgement
            {
                RequestId = envelope.RequestId, Success = false,
                Error = $"No IPC message handler is registered for '{envelope.Type}'."
            }, token).ConfigureAwait(false);
            return;
        }

        // Reserve bounded queue capacity before ACK. Execution waits for the ACK
        // to complete, but later connections never wait for a UI dispatcher here.
        var accepted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        await QueueDispatchAsync(async ct =>
        {
            if (await accepted.Task.WaitAsync(ct).ConfigureAwait(false))
                await DispatchMessageAsync(message, ct).ConfigureAwait(false);
        }, token).ConfigureAwait(false);
        try
        {
            await WriteAcknowledgementAsync(server, new IpcAcknowledgement
            {
                RequestId = envelope.RequestId, Success = true
            }, token).ConfigureAwait(false);
            accepted.TrySetResult(true);
        }
        finally { accepted.TrySetResult(false); }
    }

    private async Task DispatchMessageAsync(IpcReceivedMessage message, CancellationToken serviceToken)
    {
        if (_messageHandlers.TryGetValue(message.Type, out Func<JsonElement, CancellationToken, Task>? typedHandler))
        {
            try
            {
                await typedHandler(message.Envelope.Payload, serviceToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Debug.WriteLine(
                    $"[CFS] Typed message handler failed for '{message.Type}': {ex}");
            }
        }

        MessageReceive? handlers = OnMessageReceived;
        if (handlers is null)
        {
            return;
        }

        foreach (MessageReceive handler in handlers.GetInvocationList())
        {
            try
            {
                handler(message);
            }
            catch (Exception ex)
            {
                // Delivery has already been acknowledged. One faulty subscriber must not
                // prevent the remaining subscribers from receiving the same message.
                Debug.WriteLine(
                    $"[CFS] Message handler failed for '{message.Type}': {ex}");
            }
        }
    }

    private async Task HandleRequestAsync(
        Stream stream,
        IpcEnvelope envelope,
        CancellationToken cancellationToken)
    {
        if (!_requestHandlers.TryGetValue(envelope.Type, out Func<JsonElement, CancellationToken, Task<JsonElement>>? handler))
        {
            await WriteResponseAsync(
                stream,
                new IpcResponseEnvelope
                {
                    Type = envelope.Type,
                    RequestId = envelope.RequestId,
                    Success = false,
                    Payload = JsonSerializer.SerializeToElement(
                        new IpcNoPayload(),
                        SerializerOptions),
                    Error = $"No IPC request handler is registered for '{envelope.Type}'."
                },
                cancellationToken).ConfigureAwait(false);
            return;
        }

        try
        {
            JsonElement responsePayload = await handler(
                envelope.Payload,
                cancellationToken).ConfigureAwait(false);

            await WriteResponseAsync(
                stream,
                new IpcResponseEnvelope
                {
                    Type = envelope.Type,
                    RequestId = envelope.RequestId,
                    Success = true,
                    Payload = responsePayload
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Debug.WriteLine($"[CFS] Request handler failed for '{envelope.Type}': {ex}");
            await WriteResponseAsync(
                stream,
                new IpcResponseEnvelope
                {
                    Type = envelope.Type,
                    RequestId = envelope.RequestId,
                    Success = false,
                    Payload = JsonSerializer.SerializeToElement(
                        new IpcNoPayload(),
                        SerializerOptions),
                    Error = ex.Message
                },
                cancellationToken).ConfigureAwait(false);
        }
    }

    public bool Send(
        IpcMessageDefinition<IpcNoPayload> definition,
        TimeSpan? timeout = null) =>
        SendAsync(definition, timeout).GetAwaiter().GetResult();

    public bool Send<TPayload>(
        IpcMessageDefinition<TPayload> definition,
        TPayload payload,
        TimeSpan? timeout = null) =>
        SendAsync(definition, payload, timeout).GetAwaiter().GetResult();

    public Task<bool> SendAsync(
        IpcMessageDefinition<IpcNoPayload> definition,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default) =>
        SendAsync(definition, new IpcNoPayload(), timeout, cancellationToken);

    public async Task<bool> SendAsync<TPayload>(
        IpcMessageDefinition<TPayload> definition,
        TPayload payload,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        using OperationLease? operation = TryBeginOperation();
        if (operation is null) return false;
        timeout ??= TimeSpan.FromSeconds(5);
        using var timeoutCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, operation.Token);
        timeoutCancellation.CancelAfter(timeout.Value);
        CancellationToken operationToken = timeoutCancellation.Token;
        bool gateEntered = false;

        try
        {
            await _sendGate.WaitAsync(operationToken).ConfigureAwait(false);
            gateEntered = true;

            if (TrackedSessionExited())
            {
                return false;
            }

            IpcEnvelope envelope = CreateEnvelope(definition, payload);
            byte[] serialized = SerializeEnvelope(envelope);

            using var client = new NamedPipeClientStream(
                ".",
                SendPipeName,
                PipeDirection.InOut,
                PipeOptions.Asynchronous);
            ObjectDisposedException.ThrowIf(_disposed, this);
            await client.ConnectAsync(operationToken).ConfigureAwait(false);
            ValidateServerProcess(client);
            await WriteFrameAsync(client, serialized, operationToken).ConfigureAwait(false);

            byte[] response = await ReadFrameAsync(client, operationToken).ConfigureAwait(false);
            IpcAcknowledgement acknowledgement =
                JsonSerializer.Deserialize<IpcAcknowledgement>(response, SerializerOptions)
                ?? throw new JsonException("IPC acknowledgement is null.");

            return ValidateAcknowledgement(envelope, acknowledgement);
        }
        catch (OperationCanceledException) when (operation.Token.IsCancellationRequested
            && !cancellationToken.IsCancellationRequested)
        {
            return false;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            Debug.WriteLine($"[CFS] Timed out sending '{definition.Name}'.");
            return false;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[CFS] Failed to send '{definition.Name}': {ex.Message}");
            return false;
        }
        finally
        {
            if (gateEntered)
            {
                _sendGate.Release();
            }
        }
    }

    public Task<IpcRequestResult<TResponse>> RequestAsync<TResponse>(
        IpcRequestDefinition<IpcNoPayload, TResponse> definition,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default) =>
        RequestAsync(
            definition,
            new IpcNoPayload(),
            timeout,
            cancellationToken);

    public Task<IpcRequestResult<TResponse>> RequestAsync<TRequest, TResponse>(
        IpcRequestDefinition<TRequest, TResponse> definition,
        TRequest request,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default) =>
        RequestCoreAsync(definition, request, timeout, cancellationToken, serialize: true);

    private async Task<IpcRequestResult<TResponse>> RequestCoreAsync<TRequest, TResponse>(
        IpcRequestDefinition<TRequest, TResponse> definition,
        TRequest request,
        TimeSpan? timeout,
        CancellationToken cancellationToken,
        bool serialize,
        bool connectImmediately = false)
    {
        using OperationLease? operation = TryBeginOperation();
        if (operation is null)
            return new IpcRequestResult<TResponse>(false, default, "IPC endpoint is disposed.");
        timeout ??= TimeSpan.FromSeconds(5);
        using var timeoutCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, operation.Token);
        timeoutCancellation.CancelAfter(timeout.Value);
        CancellationToken operationToken = timeoutCancellation.Token;
        bool gateEntered = false;

        try
        {
            if (serialize)
            {
                await _sendGate.WaitAsync(operationToken).ConfigureAwait(false);
                gateEntered = true;
            }

            if (TrackedSessionExited())
            {
                return new IpcRequestResult<TResponse>(
                    false,
                    default,
                    "Target process is not running.");
            }

            IpcEnvelope envelope = CreateRequestEnvelope(definition, request);
            byte[] serialized = SerializeEnvelope(envelope);

            using var client = new NamedPipeClientStream(
                ".",
                SendPipeName,
                PipeDirection.InOut,
                PipeOptions.Asynchronous);
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (connectImmediately)
            {
                // Zero applies ONLY to acquiring a listener. Once connected,
                // the health response still has the normal operation deadline.
                await client.ConnectAsync(0, operationToken).ConfigureAwait(false);
            }
            else
            {
                await client.ConnectAsync(operationToken).ConfigureAwait(false);
            }
            ValidateServerProcess(client);
            await WriteFrameAsync(client, serialized, operationToken).ConfigureAwait(false);

            byte[] responseBytes = await ReadFrameAsync(client, operationToken).ConfigureAwait(false);
            IpcResponseEnvelope response =
                JsonSerializer.Deserialize<IpcResponseEnvelope>(responseBytes, SerializerOptions)
                ?? throw new JsonException("IPC response is null.");

            if (response.Version != IpcProtocol.CurrentVersion)
            {
                return new IpcRequestResult<TResponse>(
                    false,
                    default,
                    $"Unsupported IPC response version {response.Version}.");
            }

            if (!string.Equals(
                    envelope.RequestId,
                    response.RequestId,
                    StringComparison.Ordinal)
                || !string.Equals(
                    envelope.Type,
                    response.Type,
                    StringComparison.Ordinal))
            {
                return new IpcRequestResult<TResponse>(
                    false,
                    default,
                    "IPC response correlation failed.");
            }

            if (!response.Success)
            {
                return new IpcRequestResult<TResponse>(
                    false,
                    default,
                    response.Error);
            }

            TResponse? value = response.Payload.Deserialize<TResponse>(SerializerOptions);
            if (value is null)
            {
                return new IpcRequestResult<TResponse>(
                    false,
                    default,
                    "IPC response payload is null.");
            }

            return new IpcRequestResult<TResponse>(true, value);
        }
        catch (TimeoutException) when (connectImmediately)
        {
            // Missing/busy listener is expected during a startup preflight.
            // A tracked live process is still protected by StartProcess's guard;
            // singleton mutexes arbitrate a simultaneous external launch.
            return new IpcRequestResult<TResponse>(false, default,
                "No IPC listener is immediately available.");
        }
        catch (OperationCanceledException) when (operation.Token.IsCancellationRequested
            && !cancellationToken.IsCancellationRequested)
        {
            return new IpcRequestResult<TResponse>(false, default, "IPC endpoint is disposed.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new IpcRequestResult<TResponse>(
                false,
                default,
                $"Timed out requesting '{definition.Name}'.");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[CFS] Request '{definition.Name}' failed: {ex.Message}");
            return new IpcRequestResult<TResponse>(false, default, ex.Message);
        }
        finally
        {
            if (gateEntered)
            {
                _sendGate.Release();
            }
        }
    }

    private IpcEnvelope CreateEnvelope<TPayload>(
        IpcMessageDefinition<TPayload> definition,
        TPayload payload)
    {
        ArgumentNullException.ThrowIfNull(definition);

        return new IpcEnvelope
        {
            Version = IpcProtocol.CurrentVersion,
            Kind = IpcEnvelopeKind.Message,
            Type = definition.Name,
            RequestId = Guid.NewGuid().ToString("N"),
            Payload = JsonSerializer.SerializeToElement(payload, SerializerOptions)
        };
    }

    private IpcEnvelope CreateRequestEnvelope<TRequest, TResponse>(
        IpcRequestDefinition<TRequest, TResponse> definition,
        TRequest request)
    {
        ArgumentNullException.ThrowIfNull(definition);

        return new IpcEnvelope
        {
            Version = IpcProtocol.CurrentVersion,
            Kind = IpcEnvelopeKind.Request,
            Type = definition.Name,
            RequestId = Guid.NewGuid().ToString("N"),
            Payload = JsonSerializer.SerializeToElement(request, SerializerOptions)
        };
    }

    private byte[] SerializeEnvelope(IpcEnvelope envelope)
    {
        byte[] serialized = JsonSerializer.SerializeToUtf8Bytes(envelope, SerializerOptions);
        if (serialized.Length <= 0 || serialized.Length > MaxMessageBytes)
        {
            throw new InvalidDataException(
                $"IPC envelope size {serialized.Length} is outside the allowed range 1..{MaxMessageBytes} bytes.");
        }

        return serialized;
    }

    private void ValidateEnvelope(IpcEnvelope envelope)
    {
        if (envelope.Version != IpcProtocol.CurrentVersion)
        {
            throw new InvalidDataException(
                $"Unsupported IPC protocol version {envelope.Version}; expected {IpcProtocol.CurrentVersion}.");
        }

        if (!Enum.IsDefined(envelope.Kind))
        {
            throw new InvalidDataException(
                $"Unsupported IPC envelope kind '{envelope.Kind}'.");
        }

        if (string.IsNullOrWhiteSpace(envelope.Type))
        {
            throw new InvalidDataException("IPC message type is missing.");
        }

        if (string.IsNullOrWhiteSpace(envelope.RequestId))
        {
            throw new InvalidDataException("IPC request id is missing.");
        }

        if (envelope.Payload.ValueKind == JsonValueKind.Undefined)
        {
            throw new InvalidDataException("IPC payload is missing.");
        }
    }

    private static bool ValidateAcknowledgement(
        IpcEnvelope request,
        IpcAcknowledgement acknowledgement)
    {
        if (acknowledgement.Version != IpcProtocol.CurrentVersion)
        {
            Debug.WriteLine(
                $"[CFS] Invalid acknowledgement version {acknowledgement.Version}.");
            return false;
        }

        if (!string.Equals(
                request.RequestId,
                acknowledgement.RequestId,
                StringComparison.Ordinal))
        {
            Debug.WriteLine("[CFS] Acknowledgement request id does not match the sent message.");
            return false;
        }

        if (!acknowledgement.Success)
        {
            Debug.WriteLine($"[CFS] Remote rejected '{request.Type}': {acknowledgement.Error}");
            return false;
        }

        return true;
    }

    private async Task WriteResponseAsync(
        Stream stream,
        IpcResponseEnvelope response,
        CancellationToken cancellationToken)
    {
        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(response, SerializerOptions);
        ValidateFrameLength(payload.Length);
        await WriteFrameAsync(stream, payload, cancellationToken).ConfigureAwait(false);
    }

    private async Task WriteAcknowledgementAsync(
        Stream stream,
        IpcAcknowledgement acknowledgement,
        CancellationToken cancellationToken)
    {
        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(acknowledgement, SerializerOptions);
        ValidateFrameLength(payload.Length);
        await WriteFrameAsync(stream, payload, cancellationToken).ConfigureAwait(false);
    }

    private async Task<byte[]> ReadFrameAsync(
        Stream stream,
        CancellationToken cancellationToken)
    {
        byte[] header = new byte[sizeof(int)];
        await ReadExactlyAsync(stream, header, cancellationToken).ConfigureAwait(false);
        int length = BinaryPrimitives.ReadInt32LittleEndian(header);
        ValidateFrameLength(length);

        byte[] payload = new byte[length];
        await ReadExactlyAsync(stream, payload, cancellationToken).ConfigureAwait(false);
        return payload;
    }

    private void ValidateFrameLength(int length)
    {
        if (length <= 0 || length > MaxMessageBytes)
        {
            throw new InvalidDataException(
                $"Invalid IPC frame length {length}; allowed range is 1..{MaxMessageBytes} bytes.");
        }
    }

    private async Task WriteFrameAsync(
        Stream stream,
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken)
    {
        using var frameDeadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        frameDeadline.CancelAfter(FrameTimeout);
        cancellationToken = frameDeadline.Token;
        byte[] header = new byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(header, payload.Length);
        await stream.WriteAsync(header.AsMemory(), cancellationToken).ConfigureAwait(false);
        await stream.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task ReadExactlyAsync(
        Stream stream,
        Memory<byte> buffer,
        CancellationToken cancellationToken)
    {
        int totalRead = 0;
        while (totalRead < buffer.Length)
        {
            int read = await stream.ReadAsync(
                buffer[totalRead..],
                cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                throw new EndOfStreamException("IPC pipe closed before the complete frame was received.");
            }

            totalRead += read;
        }
    }

}
