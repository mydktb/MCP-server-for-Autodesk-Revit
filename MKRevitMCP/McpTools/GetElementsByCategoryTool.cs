using System.Text.Json;
using Autodesk.Revit.UI;

namespace MKRevitMCP.McpTools
{
    public class GetElementsByCategoryTool : ITool
    {
        public string Name => "get_elements_by_category";

        public string Execute(UIApplication app, JsonElement args)
        {
            long categoryId = Args.RequireLong(args, "categoryId");
            int maxIds = Args.GetInt(args, "maxIdsPerType", 50);
            bool groupByType = Args.GetBool(args, "groupByType", true);

            return Tools.GetElementsByCategory(app, categoryId, maxIds, groupByType);
        }
    }
}
