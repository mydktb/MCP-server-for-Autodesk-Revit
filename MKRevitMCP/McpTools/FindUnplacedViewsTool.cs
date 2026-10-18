using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace MKRevitMCP.McpTools
{
    public class FindUnplacedViewsTool : ITool
    {
        public string Name => "find_unplaced_views";

        // Not real views a user would place: browser panes, internal views, sheets themselves.
        private static readonly HashSet<ViewType> NotPlaceable = new HashSet<ViewType>
        {
            ViewType.ProjectBrowser,
            ViewType.SystemBrowser,
            ViewType.Internal,
            ViewType.Undefined,
            ViewType.DrawingSheet
        };

        public string Execute(UIApplication app, JsonElement args)
        {
            var doc = app.ActiveUIDocument?.Document;

            if (doc == null)
                return JsonSerializer.Serialize(new { error = "No document is open." });

            bool includeSchedules = Args.GetBool(args, "includeSchedules", true);
            string viewType = Args.GetString(args, "viewType");

            // Everything currently on a sheet.
            var placedViews = new HashSet<long>(
                new FilteredElementCollector(doc)
                    .OfClass(typeof(Viewport))
                    .Cast<Viewport>()
                    .Select(vp => vp.ViewId.Value));

            var placedSchedules = new HashSet<long>(
                new FilteredElementCollector(doc)
                    .OfClass(typeof(ScheduleSheetInstance))
                    .Cast<ScheduleSheetInstance>()
                    .Select(s => s.ScheduleId.Value));

            var candidates = new FilteredElementCollector(doc)
                .OfClass(typeof(View))
                .Cast<View>()
                .Where(v => !v.IsTemplate && !(v is ViewSheet) && !NotPlaceable.Contains(v.ViewType));

            if (!string.IsNullOrWhiteSpace(viewType))
            {
                candidates = candidates.Where(v =>
                    v.ViewType.ToString().Equals(viewType, StringComparison.OrdinalIgnoreCase));
            }

            long activeId = doc.ActiveView?.Id.Value ?? -1;
            var unplaced = new List<View>();

            foreach (var v in candidates)
            {
                if (v is ViewSchedule schedule)
                {
                    if (!includeSchedules || schedule.IsTitleblockRevisionSchedule) continue;
                    if (placedSchedules.Contains(v.Id.Value)) continue;
                }
                else if (placedViews.Contains(v.Id.Value))
                {
                    continue;
                }

                unplaced.Add(v);
            }

            var groups = unplaced
                .GroupBy(v => v.ViewType.ToString())
                .OrderByDescending(g => g.Count())
                .Select(g => new
                {
                    viewType = g.Key,
                    count = g.Count(),
                    views = g.OrderBy(v => v.Name).Select(v =>
                    {
                        var dependents = v.GetDependentViewIds();

                        return new
                        {
                            id = v.Id.Value,
                            name = v.Name,
                            isActiveView = v.Id.Value == activeId,
                            isDependent = v.GetPrimaryViewId() != ElementId.InvalidElementId,
                            dependentViews = dependents.Count,
                            // Deleting a parent view deletes its dependents too.
                            dependentsOnSheets = dependents.Count(d => placedViews.Contains(d.Value))
                        };
                    }).ToList()
                })
                .ToList();

            return JsonSerializer.Serialize(new
            {
                totalUnplaced = unplaced.Count,
                note = "Unplaced does not mean unwanted: working views, 3D views and coordination views are often never placed. Review before deleting.",
                groups = groups
            });
        }
    }
}
