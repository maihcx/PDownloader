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

public sealed class ConfluxService : IDisposable
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
    private readonly SemaphoreSlim _sendGate = new(1, 1);
    private readonly ConcurrentDictionary<
        string,
        Func<JsonElement, CancellationToken, Task<JsonElement>>> _requestHandlers =
        new(StringComparer.Ordinal);
    private bool _disposed;

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
        SendPipeName = sendPipeName;
        ReceivePipeName = receivePipeName;
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
        SecurityIdentifier? currentUser = WindowsIdentity.GetCurrent().User;

        if (currentUser != null)
        {
            security.AddAccessRule(new PipeAccessRule(
                currentUser,
                PipeAccessRights.ReadWrite,
                AccessControlType.Allow));
        }

        var adminsSid = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
        security.AddAccessRule(new PipeAccessRule(
            adminsSid,
            PipeAccessRights.ReadWrite,
            AccessControlType.Allow));
        return security;
    }

    public void StartApp(string argEnvironment = "")
    {
        try
        {
            if (IsAppStarted() && !CanMultiple)
            {
                return;
            }

            var psi = new ProcessStartInfo
            {
                FileName = ProcessPackage,
                UseShellExecute = false,
                Arguments = argEnvironment,
                CreateNoWindow = CreateNoWindow
            };

            Process? startedProcess = Process.Start(psi);
            lock (_processSync)
            {
                ReplaceCurrentProcess(startedProcess);
            }
        }
        catch (Exception exception)
        {
            Debug.WriteLine(
                $"[ConfluxService] Failed to start '{ProcessPackage}': {exception}");
        }
    }

    public Process GetProcess()
    {
        lock (_processSync)
        {
            if (_currProcess != null)
            {
                try
                {
                    if (!_currProcess.HasExited)
                    {
                        return _currProcess;
                    }
                }
                catch (InvalidOperationException)
                {
                    // The cached Process no longer represents an accessible process.
                }

                ReplaceCurrentProcess(null);
            }

            Process[] processes = Process.GetProcessesByName(ProcessName);
            if (processes.Length == 0)
            {
                throw new InvalidOperationException("Application is not running.");
            }

            Process selected = processes[0];
            for (int i = 1; i < processes.Length; i++)
            {
                processes[i].Dispose();
            }

            ReplaceCurrentProcess(selected);
            return selected;
        }
    }

    public bool IsAppStarted()
    {
        Process[] processes = Process.GetProcessesByName(ProcessName);
        try
        {
            return processes.Length > 0;
        }
        finally
        {
            foreach (Process process in processes)
            {
                process.Dispose();
            }
        }
    }

    private void ReplaceCurrentProcess(Process? process)
    {
        if (ReferenceEquals(_currProcess, process))
        {
            return;
        }

        _currProcess?.Dispose();
        _currProcess = process;
    }

    private async Task RunPipeServer(CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(ReceivePipeName))
        {
            throw new InvalidOperationException("A receive pipe must be registered before starting CFS.");
        }

        PipeSecurity pipeSecurity = CreateRestrictedPipeSecurity();

        while (!token.IsCancellationRequested)
        {
            try
            {
                using NamedPipeServerStream server = NamedPipeServerStreamAcl.Create(
                    ReceivePipeName,
                    PipeDirection.InOut,
                    4,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous,
                    inBufferSize: 0,
                    outBufferSize: 0,
                    pipeSecurity: pipeSecurity);

                await server.WaitForConnectionAsync(token).ConfigureAwait(false);
                await HandleServerConnectionAsync(server, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                break;
            }
            catch (IOException ex)
            {
                Debug.WriteLine($"[CFS] Pipe I/O error: {ex.Message}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[CFS] Pipe server error: {ex}");
            }
        }
    }

    private async Task HandleServerConnectionAsync(
        NamedPipeServerStream server,
        CancellationToken token)
    {
        IpcEnvelope envelope;
        try
        {
            byte[] payload = await ReadFrameAsync(server, token).ConfigureAwait(false);
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

        if (envelope.Kind == IpcEnvelopeKind.Request)
        {
            await HandleRequestAsync(server, envelope, token).ConfigureAwait(false);
            return;
        }

        try
        {
            await WriteAcknowledgementAsync(
                server,
                new IpcAcknowledgement
                {
                    RequestId = envelope.RequestId,
                    Success = true
                },
                token).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[CFS] Failed to acknowledge '{envelope.Type}': {ex.Message}");
            return;
        }

        DispatchMessage(message);
    }

    private void DispatchMessage(IpcReceivedMessage message)
    {
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
        if (!_requestHandlers.TryGetValue(envelope.Type, out var handler))
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

    public Task StartServiceAsync()
    {
        if (_cts != null)
        {
            return Task.CompletedTask;
        }

        _cts = new CancellationTokenSource();
        _serviceTask = RunPipeServer(_cts.Token);
        return Task.CompletedTask;
    }

    public async Task StopServiceAsync()
    {
        if (_cts == null)
        {
            return;
        }

        _cts.Cancel();

        try
        {
            if (_serviceTask != null)
            {
                await _serviceTask.ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            _cts.Dispose();
            _cts = null;
            _serviceTask = null;
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
        timeout ??= TimeSpan.FromSeconds(5);
        using var timeoutCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCancellation.CancelAfter(timeout.Value);
        CancellationToken operationToken = timeoutCancellation.Token;
        bool gateEntered = false;

        try
        {
            await _sendGate.WaitAsync(operationToken).ConfigureAwait(false);
            gateEntered = true;

            if (!IsAppStarted())
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
            await client.ConnectAsync(operationToken).ConfigureAwait(false);
            await WriteFrameAsync(client, serialized, operationToken).ConfigureAwait(false);

            byte[] response = await ReadFrameAsync(client, operationToken).ConfigureAwait(false);
            IpcAcknowledgement acknowledgement =
                JsonSerializer.Deserialize<IpcAcknowledgement>(response, SerializerOptions)
                ?? throw new JsonException("IPC acknowledgement is null.");

            return ValidateAcknowledgement(envelope, acknowledgement);
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

    public async Task<IpcRequestResult<TResponse>> RequestAsync<TRequest, TResponse>(
        IpcRequestDefinition<TRequest, TResponse> definition,
        TRequest request,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        timeout ??= TimeSpan.FromSeconds(5);
        using var timeoutCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCancellation.CancelAfter(timeout.Value);
        CancellationToken operationToken = timeoutCancellation.Token;
        bool gateEntered = false;

        try
        {
            await _sendGate.WaitAsync(operationToken).ConfigureAwait(false);
            gateEntered = true;

            if (!IsAppStarted())
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
            await client.ConnectAsync(operationToken).ConfigureAwait(false);
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

    private static async Task WriteFrameAsync(
        Stream stream,
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken)
    {
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

    private void Dispose(bool disposing)
    {
        if (_disposed)
        {
            return;
        }

        if (disposing)
        {
            _cts?.Cancel();
            _cts?.Dispose();

            lock (_processSync)
            {
                _currProcess?.Dispose();
                _currProcess = null;
            }

            _sendGate.Dispose();
        }

        _disposed = true;
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }
}
