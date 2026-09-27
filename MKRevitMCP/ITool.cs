using System.Text.Json;
using Autodesk.Revit.UI;

namespace MKRevitMCP
{
    public interface ITool
    {
        string Name { get; }
        string Execute(UIApplication app, JsonElement args);
    }
}