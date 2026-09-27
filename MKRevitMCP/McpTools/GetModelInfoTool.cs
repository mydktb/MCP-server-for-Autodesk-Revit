using System.Text.Json;
using Autodesk.Revit.UI;

namespace MKRevitMCP.McpTools
{
    public class GetModelInfoTool : ITool
    {
        public string Name => "get_model_info";

        public string Execute(UIApplication app, JsonElement args)
        {
            return Tools.GetModelInfo(app);
        }
    }
}
