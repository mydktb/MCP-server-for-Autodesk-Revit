using System.Text.Json;
using Autodesk.Revit.UI;

namespace MKRevitMCP.McpTools
{
    public class CountElementsByCategoryTool : ITool
    {
        public string Name => "count_elements_by_category";

        public string Execute(UIApplication app, JsonElement args)
        {
            var categoryIds = Args.GetLongList(args, "categoryIds");

            return Tools.CountElementsByCategory(app, categoryIds);
        }
    }
}
