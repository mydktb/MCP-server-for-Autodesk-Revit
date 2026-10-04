using System;
using System.Linq;
using System.Text.Json;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace MKRevitMCP.McpTools
{
    public class ListLevelsTool : ITool
    {
        public string Name => "list_levels";

        public string Execute(UIApplication app, JsonElement args)
        {
            var doc = app.ActiveUIDocument?.Document;

            if (doc == null)
                return JsonSerializer.Serialize(new { error = "No document is open." });

            var levels = new FilteredElementCollector(doc)
                .OfClass(typeof(Level))
                .Cast<Level>()
                .OrderBy(l => l.Elevation)
                .ToList();

            // Plan views that belong to a level, so callers can see which levels lack a plan.
            var planViews = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewPlan))
                .Cast<ViewPlan>()
                .Where(v => !v.IsTemplate && v.GenLevel != null)
                .ToList();

            var result = levels.Select(l => new
            {
                id = l.Id.Value,
                name = l.Name,
                elevationMm = Math.Round(UnitConvert.ToMm(l.Elevation), 1),
                planViews = planViews
                    .Where(v => v.GenLevel.Id == l.Id)
                    .OrderBy(v => v.ViewType.ToString())
                    .ThenBy(v => v.Name)
                    .Select(v => new { id = v.Id.Value, name = v.Name, type = v.ViewType.ToString() })
                    .ToList()
            }).ToList();

            return JsonSerializer.Serialize(new { count = result.Count, levels = result });
        }
    }
}
