using System;
using System.Collections.Generic;
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

                switch (tool)
                {
                    case "ping":
                        return JsonSerializer.Serialize(new { pong = true });

                    case "get_model_info":
                        return await RevitTask.RunAsync(Tools.GetModelInfo);

                    case "list_views":
                        {
                            string viewType = root.TryGetProperty("viewType", out var vt)
                                ? vt.GetString() : null;
                            string nameContains = root.TryGetProperty("nameContains", out var nc)
                                ? nc.GetString() : null;

                            return await RevitTask.RunAsync(
                                app => Tools.ListViews(app, viewType, nameContains));
                        }

                    case "rename_views":
                        {
                            var renames = new List<Tools.RenamePair>();

                            if (root.TryGetProperty("renames", out var arr))
                            {
                                foreach (var item in arr.EnumerateArray())
                                {
                                    renames.Add(new Tools.RenamePair
                                    {
                                        Id = item.GetProperty("id").GetInt64(),
                                        NewName = item.GetProperty("newName").GetString()
                                    });
                                }
                            }

                            return await RevitTask.RunAsync(
                                app => Tools.RenameViews(app, renames));
                        }

                    default:
                        return JsonSerializer.Serialize(new { error = "Unknown tool: " + tool });
                }
            }
        }
    }
}