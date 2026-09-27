using System.Collections.Generic;
using System.Text.Json;
using Autodesk.Revit.UI;

namespace MKRevitMCP.McpTools
{
    public class RenameViewsTool : ITool
    {
        public string Name => "rename_views";

        public string Execute(UIApplication app, JsonElement args)
        {
            var renames = new List<Tools.RenamePair>();

            if (args.TryGetProperty("renames", out var arr) && arr.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in arr.EnumerateArray())
                {
                    renames.Add(new Tools.RenamePair
                    {
                        Id = Args.RequireLong(item, "id"),
                        NewName = Args.RequireString(item, "newName")
                    });
                }
            }

            return Tools.RenameViews(app, renames);
        }
    }
}
