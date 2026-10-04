using System;
using System.Linq;
using System.Text.Json;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace MKRevitMCP.McpTools
{
    public class ListViewTemplatesTool : ITool
    {
        public string Name => "list_view_templates";

        public string Execute(UIApplication app, JsonElement args)
        {
            var doc = app.ActiveUIDocument?.Document;

            if (doc == null)
                return JsonSerializer.Serialize(new { error = "No document is open." });

            string viewType = Args.GetString(args, "viewType");

            var templates = new FilteredElementCollector(doc)
                .OfClass(typeof(View))
                .Cast<View>()
                .Where(v => v.IsTemplate);

            if (!string.IsNullOrWhiteSpace(viewType))
            {
                templates = templates.Where(v =>
                    v.ViewType.ToString().Equals(viewType, StringComparison.OrdinalIgnoreCase));
            }

            var result = templates
                .OrderBy(v => v.ViewType.ToString())
                .ThenBy(v => v.Name)
                .Select(v => new
                {
                    id = v.Id.Value,
                    name = v.Name,
                    appliesTo = v.ViewType.ToString()
                })
                .ToList();

            return JsonSerializer.Serialize(new { count = result.Count, templates = result });
        }
    }
}
