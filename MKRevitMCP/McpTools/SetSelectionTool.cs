using System.Text.Json;
using Autodesk.Revit.UI;

namespace MKRevitMCP.McpTools
{
    public class SetSelectionTool : ITool
    {
        public string Name => "set_selection";

        public string Execute(UIApplication app, JsonElement args)
        {
            var ids = Args.GetLongList(args, "ids");

            return Tools.SetSelection(app, ids);
        }
    }
}
