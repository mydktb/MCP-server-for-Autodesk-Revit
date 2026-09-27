using System.Text.Json;
using Autodesk.Revit.UI;

namespace MKRevitMCP.McpTools
{
    public class GetWarningsTool : ITool
    {
        public string Name => "get_warnings";

        public string Execute(UIApplication app, JsonElement args)
        {
            int maxIds = Args.GetInt(args, "maxIdsPerGroup", 20);

            return Tools.GetWarnings(app, maxIds);
        }
    }
}
