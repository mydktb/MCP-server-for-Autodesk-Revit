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

            Log.Write($"Server started on port {Port}.");
            Task.Run(() => AcceptLoop(_cts.Token));
        }

        public static void Stop()
        {
            if (!IsRunning) return;

            IsRunning = false;
            _cts.Cancel();
            _listener.Stop();
            Log.Write("Server stopped.");
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
                catch (Exception ex)
                {
                    Log.Write($"Accept loop ended: {ex.Message}");
                    break;
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
            using (var json = JsonDocument.Parse(line))
            {
                var root = json.RootElement;
                string tool = root.GetProperty("tool").GetString();

                Log.Write($"Dispatching tool: {tool}");

                // Diagnostic: answered here without touching Revit,
                // so it still works when Revit is busy or a dialog is open.
                if (tool == "ping")
                    return JsonSerializer.Serialize(new { pong = true });

                if (!ToolRegistry.TryGet(tool, out var registered))
                    return JsonSerializer.Serialize(new { error = "Unknown tool: " + tool });

                // Clone copies the arguments out of the JSON document,
                // which is disposed when this method returns.
                var args = root.Clone();
                return await RevitTask.RunAsync(app => registered.Execute(app, args));
            }
        }
    }
}
