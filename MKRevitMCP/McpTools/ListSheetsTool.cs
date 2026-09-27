using System;
using System.Linq;
using System.Text.Json;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace MKRevitMCP.McpTools
{
    public class ListSheetsTool : ITool
    {
        public string Name => "list_sheets";

        public string Execute(UIApplication app, JsonElement args)
        {
            var doc = app.ActiveUIDocument?.Document;

            if (doc == null)
                return JsonSerializer.Serialize(new { error = "No document is open." });

            string contains = Args.GetString(args, "nameContains");
            bool includeViews = Args.GetBool(args, "includeViews", true);

            var sheets = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewSheet))
                .Cast<ViewSheet>();

            if (!string.IsNullOrWhiteSpace(contains))
            {
                sheets = sheets.Where(s =>
                    s.Name.IndexOf(contains, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    s.SheetNumber.IndexOf(contains, StringComparison.OrdinalIgnoreCase) >= 0);
            }

            var result = sheets
                .OrderBy(s => s.SheetNumber)
                .Select(s => new
                {
                    id = s.Id.Value,
                    number = s.SheetNumber,
                    name = s.Name,
                    isPlaceholder = s.IsPlaceholder,
                    views = includeViews
                        ? s.GetAllPlacedViews()
                            .Select(vid => doc.GetElement(vid) as View)
                            .Where(v => v != null)
                            .Select(v => new { id = v.Id.Value, name = v.Name, type = v.ViewType.ToString() })
                            .ToList()
                        : null
                })
                .ToList();

            return JsonSerializer.Serialize(new { count = result.Count, sheets = result });
        }
    }
}
