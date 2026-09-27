using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace MKRevitMCP.McpTools
{
    public class RenameSheetsTool : ITool
    {
        public string Name => "rename_sheets";

        private class RenameRequest
        {
            public long Id;
            public string NewNumber;
            public string NewName;
            public ViewSheet Sheet;
            public string OldNumber;
            public string OldName;
        }

        public string Execute(UIApplication app, JsonElement args)
        {
            var doc = app.ActiveUIDocument?.Document;

            if (doc == null)
                return JsonSerializer.Serialize(new { error = "No document is open." });

            var requests = new List<RenameRequest>();

            if (args.TryGetProperty("renames", out var arr) && arr.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in arr.EnumerateArray())
                {
                    requests.Add(new RenameRequest
                    {
                        Id = Args.RequireLong(item, "id"),
                        NewNumber = Clean(Args.GetString(item, "number")),
                        NewName = Clean(Args.GetString(item, "name"))
                    });
                }
            }

            if (requests.Count == 0)
                return JsonSerializer.Serialize(new { error = "No renames supplied." });

            var failed = new List<object>();
            var valid = new List<RenameRequest>();

            foreach (var r in requests)
            {
                r.Sheet = doc.GetElement(new ElementId(r.Id)) as ViewSheet;

                if (r.Sheet == null)
                {
                    failed.Add(new { id = r.Id, reason = "Not a sheet, or id not found." });
                    continue;
                }

                if (r.NewNumber == null && r.NewName == null)
                {
                    failed.Add(new { id = r.Id, reason = "Nothing to change - supply a number and/or a name." });
                    continue;
                }

                r.OldNumber = r.Sheet.SheetNumber;
                r.OldName = r.Sheet.Name;
                valid.Add(r);
            }

            // Sheet numbers must be unique. Check against every sheet that is NOT being renumbered.
            var renumberIds = new HashSet<long>(valid.Where(r => r.NewNumber != null).Select(r => r.Id));
            var heldNumbers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var s in new FilteredElementCollector(doc).OfClass(typeof(ViewSheet)).Cast<ViewSheet>())
            {
                if (!renumberIds.Contains(s.Id.Value))
                    heldNumbers[s.SheetNumber] = s.Name;
            }

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var r in valid.Where(r => r.NewNumber != null).ToList())
            {
                if (heldNumbers.TryGetValue(r.NewNumber, out var holder))
                {
                    failed.Add(new { id = r.Id, reason = $"Number {r.NewNumber} is already used by sheet '{holder}'." });
                    valid.Remove(r);
                }
                else if (!seen.Add(r.NewNumber))
                {
                    failed.Add(new { id = r.Id, reason = $"Number {r.NewNumber} appears twice in this request." });
                    valid.Remove(r);
                }
            }

            if (valid.Count == 0)
                return JsonSerializer.Serialize(new { renamed = 0, failedCount = failed.Count, succeeded = new List<object>(), failed = failed });

            using (var tx = new Transaction(doc, "Rename sheets"))
            {
                tx.Start();

                try
                {
                    // Two passes, so swaps work: A101<->A102 would collide if done in one step.
                    foreach (var r in valid.Where(r => r.NewNumber != null))
                        r.Sheet.SheetNumber = "__mk_tmp_" + r.Id;

                    foreach (var r in valid.Where(r => r.NewNumber != null))
                        r.Sheet.SheetNumber = r.NewNumber;

                    foreach (var r in valid.Where(r => r.NewName != null))
                        r.Sheet.Name = r.NewName;

                    tx.Commit();
                }
                catch (Exception ex)
                {
                    tx.RollBack();

                    return JsonSerializer.Serialize(new
                    {
                        error = "Rename rolled back, no sheets were changed: " + ex.Message,
                        failed = failed
                    });
                }
            }

            var succeeded = valid.Select(r => new
            {
                id = r.Id,
                oldNumber = r.OldNumber,
                newNumber = r.NewNumber ?? r.OldNumber,
                oldName = r.OldName,
                newName = r.NewName ?? r.OldName
            }).ToList();

            return JsonSerializer.Serialize(new
            {
                renamed = succeeded.Count,
                failedCount = failed.Count,
                succeeded = succeeded,
                failed = failed
            });
        }

        private static string Clean(string s)
        {
            return string.IsNullOrWhiteSpace(s) ? null : s.Trim();
        }
    }
}
