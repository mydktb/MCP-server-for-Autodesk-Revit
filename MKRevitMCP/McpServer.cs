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
                    case "get_warnings":
                        {
                            int maxIds = root.TryGetProperty("maxIdsPerGroup", out var mi)
                                ? mi.GetInt32() : 20;

                            return await RevitTask.RunAsync(
                                app => Tools.GetWarnings(app, maxIds));
                        }

                    case "set_selection":
                        {
                            var ids = new List<long>();

                            if (root.TryGetProperty("ids", out var idArr))
                            {
                                foreach (var item in idArr.EnumerateArray())
                                    ids.Add(item.GetInt64());
                            }

                            return await RevitTask.RunAsync(
                                app => Tools.SetSelection(app, ids));
                        }
                    case "get_categories_by_keywords":
                        {
                            var keywords = new List<string>();

                            if (root.TryGetProperty("keywords", out var kw))
                            {
                                foreach (var item in kw.EnumerateArray())
                                    keywords.Add(item.GetString());
                            }

                            return await RevitTask.RunAsync(
                                app => Tools.GetCategoriesByKeywords(app, keywords));
                        }

                    case "count_elements_by_category":
                        {
                            var catIds = new List<long>();

                            if (root.TryGetProperty("categoryIds", out var ci))
                            {
                                foreach (var item in ci.EnumerateArray())
                                    catIds.Add(item.GetInt64());
                            }

                            return await RevitTask.RunAsync(
                                app => Tools.CountElementsByCategory(app, catIds));
                        }

                    case "get_elements_by_category":
                        {
                            long catId = root.GetProperty("categoryId").GetInt64();

                            int maxIds = root.TryGetProperty("maxIdsPerType", out var mx)
                                ? mx.GetInt32() : 50;

                            bool byType = !root.TryGetProperty("groupByType", out var gb)
                                || gb.GetBoolean();

                            return await RevitTask.RunAsync(
                                app => Tools.GetElementsByCategory(app, catId, maxIds, byType));
                        }

                    case "get_element_parameters":
                        {
                            long elId = root.GetProperty("elementId").GetInt64();

                            bool incType = !root.TryGetProperty("includeType", out var it)
                                || it.GetBoolean();

                            return await RevitTask.RunAsync(
                                app => Tools.GetElementParameters(app, elId, incType));
                        }

                    case "get_parameter_values":
                        {
                            var ids = new List<long>();
                            if (root.TryGetProperty("elementIds", out var ia))
                                foreach (var item in ia.EnumerateArray())
                                    ids.Add(item.GetInt64());

                            var names = new List<string>();
                            if (root.TryGetProperty("parameterNames", out var na))
                                foreach (var item in na.EnumerateArray())
                                    names.Add(item.GetString());

                            bool grouped = !root.TryGetProperty("groupByValue", out var gv)
                                || gv.GetBoolean();

                            return await RevitTask.RunAsync(
                                app => Tools.GetParameterValues(app, ids, names, grouped));
                        }

                    case "set_parameter_value":
                        {
                            var ids = new List<long>();
                            if (root.TryGetProperty("elementIds", out var sa))
                                foreach (var item in sa.EnumerateArray())
                                    ids.Add(item.GetInt64());

                            string pName = root.GetProperty("parameterName").GetString();
                            string pValue = root.GetProperty("newValue").GetString();

                            bool dry = !root.TryGetProperty("dryRun", out var dr)
                                || dr.GetBoolean();

                            return await RevitTask.RunAsync(
                                app => Tools.SetParameterValue(app, ids, pName, pValue, dry));
                        }

                    default:
                        return JsonSerializer.Serialize(new { error = "Unknown tool: " + tool });
                }
            }
        }
    }
}