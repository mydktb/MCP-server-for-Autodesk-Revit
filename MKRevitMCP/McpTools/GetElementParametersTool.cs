using System.Text.Json;
using Autodesk.Revit.UI;

namespace MKRevitMCP.McpTools
{
    public class GetElementParametersTool : ITool
    {
        public string Name => "get_element_parameters";

        public string Execute(UIApplication app, JsonElement args)
        {
            long elementId = Args.RequireLong(args, "elementId");
            bool includeType = Args.GetBool(args, "includeType", true);

            return Tools.GetElementParameters(app, elementId, includeType);
        }
    }
}
