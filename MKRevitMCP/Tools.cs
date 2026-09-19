using System.Linq;
using System.Text.Json;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace MKRevitMCP
{
    public static class Tools
    {
        public static string GetModelInfo(UIApplication app)
        {
            var doc = app.ActiveUIDocument?.Document;

            if (doc == null)
                return JsonSerializer.Serialize(new { error = "No document is open." });

            int viewCount = new FilteredElementCollector(doc)
                .OfClass(typeof(View))
                .Cast<View>()
                .Count(v => !v.IsTemplate);

            return JsonSerializer.Serialize(new
            {
                title = doc.Title,
                path = doc.PathName,
                activeView = doc.ActiveView?.Name,
                viewCount = viewCount
            });
        }
    }
}