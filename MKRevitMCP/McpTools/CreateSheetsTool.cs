using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace MKRevitMCP.McpTools
{
    public class CreateSheetsTool : ITool
    {
        public string Name => "create_sheets";

        private class SheetRequest
        {
            public string Number;
            public string SheetName;
        }

        public string Execute(UIApplication app, JsonElement args)
        {
            var doc = app.ActiveUIDocument?.Document;

            if (doc == null)
                return JsonSerializer.Serialize(new { error = "No document is open." });

            var requests = new List<SheetRequest>();

            if (args.TryGetProperty("sheets", out var arr) && arr.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in arr.EnumerateArray())
                {
                    requests.Add(new SheetRequest
                    {
                        Number = Clean(Args.GetString(item, "number")),
                        SheetName = Args.RequireString(item, "name")
                    });
                }
            }

            if (requests.Count == 0)
                return JsonSerializer.Serialize(new { error = "No sheets supplied." });

            // Resolve the title block: the one asked for, else the first available, else none.
            var titleBlocks = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_TitleBlocks)
                .WhereElementIsElementType()
                .OfType<FamilySymbol>()
                .OrderBy(t => t.FamilyName)
                .ThenBy(t => t.Name)
                .ToList();

            long? requestedTb = Args.GetLongOrNull(args, "titleBlockTypeId");
            ElementId titleBlockId;
            string titleBlockLabel;

            if (requestedTb.HasValue)
            {
                var chosen = titleBlocks.FirstOrDefault(t => t.Id.Value == requestedTb.Value);

                if (chosen == null)
                {
                    return JsonSerializer.Serialize(new
                    {
                        error = $"{requestedTb.Value} is not a title block type. Use list_titleblocks to find valid ids."
                    });
                }

                titleBlockId = chosen.Id;
                titleBlockLabel = $"{chosen.FamilyName} : {chosen.Name}";
            }
            else if (titleBlocks.Count > 0)
            {
                titleBlockId = titleBlocks[0].Id;
                titleBlockLabel = $"{titleBlocks[0].FamilyName} : {titleBlocks[0].Name} (default - none was specified)";
            }
            else
            {
                titleBlockId = ElementId.InvalidElementId;
                titleBlockLabel = "(none - the model has no title blocks loaded)";
            }

            var existingNumbers = new HashSet<string>(
                new FilteredElementCollector(doc)
                    .OfClass(typeof(ViewSheet))
                    .Cast<ViewSheet>()
                    .Select(s => s.SheetNumber),
                StringComparer.OrdinalIgnoreCase);

            var seenInRequest = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var succeeded = new List<object>();
            var failed = new List<object>();

            using (var tx = new Transaction(doc, "Create sheets"))
            {
                tx.Start();

                foreach (var r in requests)
                {
                    if (r.Number != null &&
                        (existingNumbers.Contains(r.Number) || !seenInRequest.Add(r.Number)))
                    {
                        failed.Add(new { number = r.Number, name = r.SheetName, reason = "Sheet number is already in use." });
                        continue;
                    }

                    ViewSheet sheet = null;

                    try
                    {
                        sheet = ViewSheet.Create(doc, titleBlockId);

                        if (r.Number != null)
                            sheet.SheetNumber = r.Number;

                        sheet.Name = r.SheetName;

                        succeeded.Add(new
                        {
                            id = sheet.Id.Value,
                            number = sheet.SheetNumber,
                            name = sheet.Name
                        });
                    }
                    catch (Exception ex)
                    {
                        // Don't leave a half-configured sheet behind.
                        if (sheet != null)
                        {
                            try { doc.Delete(sheet.Id); } catch { }
                        }

                        failed.Add(new { number = r.Number, name = r.SheetName, reason = ex.Message });
                    }
                }

                if (succeeded.Count > 0)
                    tx.Commit();
                else
                    tx.RollBack();
            }

            return JsonSerializer.Serialize(new
            {
                created = succeeded.Count,
                failedCount = failed.Count,
                titleBlock = titleBlockLabel,
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
