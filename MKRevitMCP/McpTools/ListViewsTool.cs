using System.Text.Json;
using Autodesk.Revit.UI;

namespace MKRevitMCP.McpTools
{
    public class ListViewsTool : ITool
    {
        public string Name => "list_views";

        public string Execute(UIApplication app, JsonElement args)
        {
            string viewType = Args.GetString(args, "viewType");
            string nameContains = Args.GetString(args, "nameContains");

            return Tools.ListViews(app, viewType, nameContains);
        }
    }
}
