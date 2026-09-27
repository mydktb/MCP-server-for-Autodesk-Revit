using System.Text.Json;
using Autodesk.Revit.UI;

namespace MKRevitMCP.McpTools
{
    public class GetParameterValuesTool : ITool
    {
        public string Name => "get_parameter_values";

        public string Execute(UIApplication app, JsonElement args)
        {
            var elementIds = Args.GetLongList(args, "elementIds");
            var parameterNames = Args.GetStringList(args, "parameterNames");
            bool groupByValue = Args.GetBool(args, "groupByValue", true);

            return Tools.GetParameterValues(app, elementIds, parameterNames, groupByValue);
        }
    }
}
