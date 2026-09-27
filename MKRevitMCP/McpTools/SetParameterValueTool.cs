using System.Text.Json;
using Autodesk.Revit.UI;

namespace MKRevitMCP.McpTools
{
    public class SetParameterValueTool : ITool
    {
        public string Name => "set_parameter_value";

        public string Execute(UIApplication app, JsonElement args)
        {
            var elementIds = Args.GetLongList(args, "elementIds");
            string parameterName = Args.RequireString(args, "parameterName");
            string newValue = Args.RequireString(args, "newValue");
            bool dryRun = Args.GetBool(args, "dryRun", true);

            return Tools.SetParameterValue(app, elementIds, parameterName, newValue, dryRun);
        }
    }
}
