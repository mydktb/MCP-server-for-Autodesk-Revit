using System.Linq;
using System.Text.Json;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace MKRevitMCP.McpTools
{
    public class ListTitleBlocksTool : ITool
    {
        public string Name => "list_titleblocks";

        public string Execute(UIApplication app, JsonElement args)
        {
            var doc = app.ActiveUIDocument?.Document;

            if (doc == null)
                return JsonSerializer.Serialize(new { error = "No document is open." });

            var types = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_TitleBlocks)
                .WhereElementIsElementType()
                .OfType<FamilySymbol>()
                .OrderBy(t => t.FamilyName)
                .ThenBy(t => t.Name)
                .Select(t => new
                {
                    id = t.Id.Value,
                    family = t.FamilyName,
                    type = t.Name
                })
                .ToList();

            return JsonSerializer.Serialize(new { count = types.Count, titleBlocks = types });
        }
    }
}
