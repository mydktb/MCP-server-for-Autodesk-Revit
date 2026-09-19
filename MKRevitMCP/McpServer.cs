using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace MKRevitMCP
{
    public static class McpServer
    {
        public const int Port = 5566;
        public static bool IsRunning { get; private set; }

        private static TcpListener _listener;
        private static CancellationTokenSource _cts;

        public static void Start()
        {
            if (IsRunning) return;

            _cts = new CancellationTokenSource();
            _listener = new TcpListener(IPAddress.Loopback, Port);
            _listener.Start();
            IsRunning = true;

            Task.Run(() => AcceptLoop(_cts.Token));
        }

        public static void Stop()
        {
            if (!IsRunning) return;

            IsRunning = false;
            _cts.Cancel();
            _listener.Stop();
        }

        private static async Task AcceptLoop(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                TcpClient client;
                try
                {
                    client = await _listener.AcceptTcpClientAsync();
                }
                catch
                {
                    break; // listener stopped
                }

                _ = Task.Run(() => HandleClient(client));
            }
        }

        private static async Task HandleClient(TcpClient client)
        {
            try
            {
                using (client)
                using (var stream = client.GetStream())
                using (var reader = new StreamReader(stream))
                using (var writer = new StreamWriter(stream) { AutoFlush = true })
                {
                    string line;
                    while ((line = await reader.ReadLineAsync()) != null)
                    {
                        if (string.IsNullOrWhiteSpace(line)) continue;

                        Log.Write($"Line received: {line}");

                        string response;
                        try
                        {
                            response = await Dispatch(line);
                        }
                        catch (Exception ex)
                        {
                            Log.Write($"Dispatch threw: {ex}");
                            response = JsonSerializer.Serialize(new { error = ex.Message });
                        }

                        await writer.WriteLineAsync(response);
                        Log.Write("Response written.");
                    }

                    Log.Write("Client disconnected.");
                }
            }
            catch (IOException)
            {
                Log.Write("Client aborted the connection.");
            }
            catch (Exception ex)
            {
                Log.Write($"HandleClient threw: {ex}");
            }
        }

        private static async Task<string> Dispatch(string line)
        {
            string tool;

            using (var json = JsonDocument.Parse(line))
            {
                tool = json.RootElement.GetProperty("tool").GetString();
            }

            switch (tool)
            {
                case "ping":
                    return JsonSerializer.Serialize(new { pong = true });

                case "get_model_info":
                    return await RevitTask.RunAsync(Tools.GetModelInfo);

                default:
                    return JsonSerializer.Serialize(new { error = "Unknown tool: " + tool });
            }
        }
    }
}