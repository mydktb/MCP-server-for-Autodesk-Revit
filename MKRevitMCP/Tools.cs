using System.Collections.Generic;
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

        public static string ListViews(UIApplication app, string viewType, string nameContains)
        {
            var doc = app.ActiveUIDocument?.Document;

            if (doc == null)
                return JsonSerializer.Serialize(new { error = "No document is open." });

            var views = new FilteredElementCollector(doc)
                .OfClass(typeof(View))
                .Cast<View>()
                .Where(v => !v.IsTemplate);

            if (!string.IsNullOrWhiteSpace(viewType))
            {
                views = views.Where(v =>
                    v.ViewType.ToString().Equals(viewType, System.StringComparison.OrdinalIgnoreCase));
            }

            if (!string.IsNullOrWhiteSpace(nameContains))
            {
                views = views.Where(v =>
                    v.Name.IndexOf(nameContains, System.StringComparison.OrdinalIgnoreCase) >= 0);
            }

            var result = views
                .OrderBy(v => v.ViewType.ToString())
                .ThenBy(v => v.Name)
                .Select(v => new
                {
                    id = v.Id.Value,
                    name = v.Name,
                    type = v.ViewType.ToString()
                })
                .ToList();

            return JsonSerializer.Serialize(new { count = result.Count, views = result });
        }

        public static string RenameViews(UIApplication app, List<RenamePair> renames)
        {
            var doc = app.ActiveUIDocument?.Document;

            if (doc == null)
                return JsonSerializer.Serialize(new { error = "No document is open." });

            if (renames == null || renames.Count == 0)
                return JsonSerializer.Serialize(new { error = "No renames supplied." });

            var succeeded = new List<object>();
            var failed = new List<object>();

            using (var tx = new Transaction(doc, "Rename views"))
            {
                tx.Start();

                foreach (var pair in renames)
                {
                    try
                    {
                        var view = doc.GetElement(new ElementId(pair.Id)) as View;

                        if (view == null)
                        {
                            failed.Add(new { id = pair.Id, reason = "Not a view, or id not found." });
                            continue;
                        }

                        string oldName = view.Name;
                        view.Name = pair.NewName;

                        succeeded.Add(new { id = pair.Id, oldName = oldName, newName = pair.NewName });
                    }
                    catch (System.Exception ex)
                    {
                        failed.Add(new { id = pair.Id, reason = ex.Message });
                    }
                }

                if (succeeded.Count > 0)
                    tx.Commit();
                else
                    tx.RollBack();
            }

            return JsonSerializer.Serialize(new
            {
                renamed = succeeded.Count,
                failedCount = failed.Count,
                succeeded = succeeded,
                failed = failed
            });
        }

        public class RenamePair
        {
            public long Id { get; set; }
            public string NewName { get; set; }
        }
    }
}