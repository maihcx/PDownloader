using System.Diagnostics;
using System.IO.Pipes;
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
        private Process? _currProcess { get; set; }

        public bool CreateNoWindow = false;

        private CancellationTokenSource? _cts;
        private Task? _serviceTask;

        public void Register(string processPackage, string pipeSend, string? pipeReceive = null)
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
        }

        public void StartApp(string argEnvironment = "")
        {
            try
            {
                if (IsAppStarted())
                {
                    return;
                }
                var psi = new ProcessStartInfo
                {
                    FileName = ProcessPackage,
                    UseShellExecute = false,
                    Arguments = "--cfx-mode " + argEnvironment,
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
            while (!token.IsCancellationRequested)
            {
                try
                {
                    using var server = new NamedPipeServerStream(PipeGet, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);

                    await server.WaitForConnectionAsync(token);

                    byte[] buffer = new byte[4096];
                    int bytesRead = await server.ReadAsync(buffer.AsMemory(0, buffer.Length), token);
                    string message = Encoding.UTF8.GetString(buffer, 0, bytesRead);

                    var parts = message.Split('|', 2);
                    string paramName = parts[0];
                    string paramValue = parts.Length > 1 ? parts[1] : "";

                    OnMessageReceiving?.Invoke(paramName, paramValue);
                    SendBitOK();
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

        public async Task StartServiceAsync()
        {
            if (_cts != null)
            {
                return;
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

        private void SendBitOK()
        {
            try
            {
                using var responsePipe = new NamedPipeServerStream(PipeGetReceive, PipeDirection.Out);
                responsePipe.WaitForConnection();
                byte[] response = Encoding.UTF8.GetBytes("OK");
                responsePipe.Write(response, 0, response.Length);
                responsePipe.Flush();
            }
            catch { }
        }

        public bool Send(string paramName, string paramValue, int timeoutMs = 5000)
        {
            try
            {
                if (IsAppStarted())
                {
                    using var client = new NamedPipeClientStream(".", PipeSend, PipeDirection.Out);
                    client.Connect();
                    byte[] buffer = Encoding.UTF8.GetBytes($"{paramName}|{paramValue}");
                    client.Write(buffer, 0, buffer.Length);
                    client.Flush();

                    using var responseClient = new NamedPipeClientStream(".", PipeSendReceive, PipeDirection.In);
                    responseClient.Connect(timeoutMs);

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
