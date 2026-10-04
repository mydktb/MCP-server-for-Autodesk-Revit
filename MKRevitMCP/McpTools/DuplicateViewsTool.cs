using System;
using System.Collections.Generic;
using System.Text.Json;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace MKRevitMCP.McpTools
{
    public class DuplicateViewsTool : ITool
    {
        public string Name => "duplicate_views";

        private class DuplicateRequest
        {
            public long ViewId;
            public string NewName;
        }

        public string Execute(UIApplication app, JsonElement args)
        {
            var doc = app.ActiveUIDocument?.Document;

            if (doc == null)
                return JsonSerializer.Serialize(new { error = "No document is open." });

            var requests = new List<DuplicateRequest>();

            if (args.TryGetProperty("views", out var arr) && arr.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in arr.EnumerateArray())
                {
                    string n = Args.GetString(item, "newName");

                    requests.Add(new DuplicateRequest
                    {
                        ViewId = Args.RequireLong(item, "viewId"),
                        NewName = string.IsNullOrWhiteSpace(n) ? null : n.Trim()
                    });
                }
            }

            if (requests.Count == 0)
                return JsonSerializer.Serialize(new { error = "No views supplied." });

            string mode = Args.GetString(args, "mode", "duplicate");
            ViewDuplicateOption option;

            switch (mode.Trim().ToLowerInvariant())
            {
                case "duplicate": option = ViewDuplicateOption.Duplicate; break;
                case "with_detailing": option = ViewDuplicateOption.WithDetailing; break;
                case "dependent": option = ViewDuplicateOption.AsDependent; break;
                default:
                    return JsonSerializer.Serialize(new
                    {
                        error = $"Unknown mode '{mode}'. Use duplicate, with_detailing or dependent."
                    });
            }

            var succeeded = new List<object>();
            var failed = new List<object>();

            using (var tx = new Transaction(doc, "Duplicate views"))
            {
                tx.Start();

                foreach (var r in requests)
                {
                    var view = doc.GetElement(new ElementId(r.ViewId)) as View;

                    if (view == null)
                    {
                        failed.Add(new { viewId = r.ViewId, reason = "Not a view, or id not found." });
                        continue;
                    }

                    if (view.IsTemplate)
                    {
                        failed.Add(new { viewId = r.ViewId, reason = "View templates cannot be duplicated this way." });
                        continue;
                    }

                    if (!view.CanViewBeDuplicated(option))
                    {
                        failed.Add(new
                        {
                            viewId = r.ViewId,
                            viewName = view.Name,
                            reason = $"This view cannot be duplicated as '{mode}'."
                        });
                        continue;
                    }

                    ElementId newId = null;

                    try
                    {
                        newId = view.Duplicate(option);
                        var newView = doc.GetElement(newId) as View;

                        if (r.NewName != null)
                            newView.Name = r.NewName;

                        succeeded.Add(new
                        {
                            sourceId = r.ViewId,
                            sourceName = view.Name,
                            newId = newId.Value,
                            newName = newView.Name
                        });
                    }
                    catch (Exception ex)
                    {
                        if (newId != null && newId != ElementId.InvalidElementId)
                        {
                            try { doc.Delete(newId); } catch { }
                        }

                        failed.Add(new { viewId = r.ViewId, viewName = view.Name, reason = ex.Message });
                    }
                }

                if (succeeded.Count > 0)
                    tx.Commit();
                else
                    tx.RollBack();
            }

            return JsonSerializer.Serialize(new
            {
                duplicated = succeeded.Count,
                failedCount = failed.Count,
                mode = mode,
                succeeded = succeeded,
                failed = failed
            });
        }
    }
}
