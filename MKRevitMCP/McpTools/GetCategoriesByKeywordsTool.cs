using System.Text.Json;
using Autodesk.Revit.UI;

namespace MKRevitMCP.McpTools
{
    public class GetCategoriesByKeywordsTool : ITool
    {
        public string Name => "get_categories_by_keywords";

        public string Execute(UIApplication app, JsonElement args)
        {
            var keywords = Args.GetStringList(args, "keywords");

            return Tools.GetCategoriesByKeywords(app, keywords);
        }
    }
}
