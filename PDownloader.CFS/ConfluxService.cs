using System.Diagnostics;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;

namespace PDownloader.CFS
{
    public class ConfluxService
    {
        public delegate void MessageReceive(string type, string message);
        public MessageReceive? OnMessageReceived;
        public MessageReceive? OnMessageReceiving;

        public string ProcessPackage { get; set; } = string.Empty;
        public string ProcessName { get; set; } = string.Empty;
        public string PipeSend { get; set; } = string.Empty;
        public string PipeSendReceive { get; set; } = string.Empty;
        public string PipeGet { get; set; } = string.Empty;
        public string PipeGetReceive { get; set; } = string.Empty;
        public bool CanMultiple { get; set; } = false;
        private Process? _currProcess { get; set; }

        public bool CreateNoWindow = false;

        public string AuthToken { get; private set; } = string.Empty;

        public int MaxMessageBytes { get; set; } = 1024 * 1024; // 1MB

        private CancellationTokenSource? _cts;
        private Task? _serviceTask;

        public static string GenerateToken()
        {
            Span<byte> bytes = stackalloc byte[32];
            RandomNumberGenerator.Fill(bytes);
            return Convert.ToBase64String(bytes)
                .Replace('+', '-')
                .Replace('/', '_')
                .TrimEnd('=');
        }

        private const string TokenArgName = "--cfx-token";

        public static string? ExtractTokenFromCurrentProcessArgs()
        {
            return ExtractTokenFromArgs(Environment.GetCommandLineArgs());
        }

        public static string? ExtractTokenFromArgs(string[] args)
        {
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (string.Equals(args[i], TokenArgName, StringComparison.Ordinal))
                {
                    return args[i + 1];
                }
            }
            return null;
        }

        public void Register(string processPackage, string pipeSend, string? pipeReceive = null, string? authToken = null)
        {
            ProcessPackage = processPackage;
            ProcessName = processPackage.Replace(".exe", "");

            PipeSend = pipeSend;
            PipeSendReceive = pipeSend + "Response";

            if (pipeReceive != null)
            {
                PipeGet = pipeReceive;
            }
            PipeGetReceive = pipeReceive + "Response";

            AuthToken = authToken
                ?? ExtractTokenFromCurrentProcessArgs()
                ?? GenerateToken();
        }

        private static PipeSecurity CreateRestrictedPipeSecurity()
        {
            var security = new PipeSecurity();
            var currentUser = WindowsIdentity.GetCurrent().User;

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

                if (string.IsNullOrEmpty(AuthToken))
                {
                    AuthToken = GenerateToken();
                }

                var psi = new ProcessStartInfo
                {
                    FileName = ProcessPackage,
                    UseShellExecute = false,

                    Arguments = $"--cfx-token {AuthToken} {argEnvironment}",
                    CreateNoWindow = CreateNoWindow
                };
                _currProcess = Process.Start(psi);
            }
            catch { }
        }

        public Process GetProcess()
        {
            if (_currProcess != null && !_currProcess.HasExited)
            {
                return _currProcess;
            }
            var processes = Process.GetProcessesByName(ProcessName);
            if (processes.Length > 0)
            {
                _currProcess = processes[0];
                return _currProcess;
            }
            throw new InvalidOperationException("Application is not running.");
        }

        public bool IsAppStarted()
        {
            return Process.GetProcessesByName(ProcessName).Length > 0;
        }

        private async Task RunPipeServer(CancellationToken token)
        {
            var pipeSecurity = CreateRestrictedPipeSecurity();

            while (!token.IsCancellationRequested)
            {
                try
                {
                    using var server = NamedPipeServerStreamAcl.Create(
                        PipeGet,
                        PipeDirection.In,
                        1,
                        PipeTransmissionMode.Byte,
                        PipeOptions.Asynchronous,
                        inBufferSize: 0,
                        outBufferSize: 0,
                        pipeSecurity: pipeSecurity);

                    await server.WaitForConnectionAsync(token);

                    byte[] buffer = new byte[MaxMessageBytes];
                    int bytesRead = await server.ReadAsync(buffer.AsMemory(0, buffer.Length), token);

                    if (bytesRead <= 0)
                    {
                        server.Disconnect();
                        continue;
                    }

                    string raw = Encoding.UTF8.GetString(buffer, 0, bytesRead);

                    var parts = raw.Split('|', 3);
                    if (parts.Length < 2)
                    {
                        server.Disconnect();
                        continue;
                    }

                    string receivedToken = parts[0];
                    string paramName;
                    string paramValue;

                    if (parts.Length == 3)
                    {
                        paramName = parts[1];
                        paramValue = parts[2];
                    }
                    else
                    {
                        server.Disconnect();
                        continue;
                    }

                    if (!ValidateToken(receivedToken))
                    {
                        Debug.WriteLine("[PipeServer] Token không hợp lệ - message bị từ chối.");
                        server.Disconnect();
                        continue;
                    }

                    OnMessageReceiving?.Invoke(paramName, paramValue);
                    await SendBitOKAsync(token);
                    OnMessageReceived?.Invoke(paramName, paramValue);

                    server.Disconnect();
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (IOException)
                {

                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[PipeServer] Lỗi: {ex}");
                }
            }
        }

        private bool ValidateToken(string receivedToken)
        {
            if (string.IsNullOrEmpty(AuthToken) || string.IsNullOrEmpty(receivedToken))
            {
                return false;
            }

            byte[] expected = Encoding.UTF8.GetBytes(AuthToken);
            byte[] actual = Encoding.UTF8.GetBytes(receivedToken);

            if (expected.Length != actual.Length)
            {
                return CryptographicOperations.FixedTimeEquals(expected, expected) && false;
            }

            return CryptographicOperations.FixedTimeEquals(expected, actual);
        }

        public async Task StartServiceAsync()
        {
            if (_cts != null)
            {
                return;
            }

            if (string.IsNullOrEmpty(AuthToken))
            {
                AuthToken = GenerateToken();
            }

            _cts = new CancellationTokenSource();
            _serviceTask = RunPipeServer(_cts.Token);
        }

        public async Task StopServiceAsync()
        {
            if (_cts == null)
                return;

            _cts.Cancel();

            try
            {
                if (_serviceTask != null)
                {
                    await _serviceTask;
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

        private async Task SendBitOKAsync(CancellationToken token)
        {
            try
            {
                var pipeSecurity = CreateRestrictedPipeSecurity();

                using var responsePipe = NamedPipeServerStreamAcl.Create(
                    PipeGetReceive,
                    PipeDirection.Out,
                    1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous,
                    inBufferSize: 0,
                    outBufferSize: 0,
                    pipeSecurity: pipeSecurity);

                await responsePipe.WaitForConnectionAsync(token);

                byte[] response = Encoding.UTF8.GetBytes("OK");
                await responsePipe.WriteAsync(response, token);
                await responsePipe.FlushAsync(token);
            }
            catch (OperationCanceledException)
            {
            }
            catch
            {
            }
        }

        public bool Send(string paramName, string paramValue, TimeSpan? timeout = null)
        {
            if (timeout == null)
            {
                timeout = TimeSpan.FromSeconds(5);
            }

            if (string.IsNullOrEmpty(AuthToken))
            {
                Debug.WriteLine("[Send] AuthToken chưa được thiết lập - từ chối gửi.");
                return false;
            }

            try
            {
                if (IsAppStarted())
                {
                    using var client = new NamedPipeClientStream(".", PipeSend, PipeDirection.Out);
                    client.Connect((int)timeout.Value.TotalMilliseconds);

                    byte[] buffer = Encoding.UTF8.GetBytes($"{AuthToken}|{paramName}|{paramValue}");
                    if (buffer.Length > MaxMessageBytes)
                    {
                        Debug.WriteLine("[Send] Message vượt quá kích thước cho phép.");
                        return false;
                    }

                    client.Write(buffer, 0, buffer.Length);
                    client.Flush();

                    using var responseClient = new NamedPipeClientStream(".", PipeSendReceive, PipeDirection.In);
                    responseClient.Connect((int)timeout.Value.TotalMilliseconds);

                    byte[] responseBuffer = new byte[256];
                    int bytesRead = responseClient.Read(responseBuffer, 0, responseBuffer.Length);
                    string response = Encoding.UTF8.GetString(responseBuffer, 0, bytesRead);

                    return response.Trim() == "OK";
                }
                else
                {
                    return false;
                }
            }
            catch { }
            return false;
        }
    }
}