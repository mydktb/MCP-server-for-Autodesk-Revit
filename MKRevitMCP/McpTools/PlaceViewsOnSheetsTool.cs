using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace MKRevitMCP.McpTools
{
    public class PlaceViewsOnSheetsTool : ITool
    {
        public string Name => "place_views_on_sheets";

        private class PlacementRequest
        {
            public long ViewId;
            public long SheetId;
            public double? XMm;
            public double? YMm;
        }

        public string Execute(UIApplication app, JsonElement args)
        {
            var doc = app.ActiveUIDocument?.Document;

            if (doc == null)
                return JsonSerializer.Serialize(new { error = "No document is open." });

            var requests = new List<PlacementRequest>();

            if (args.TryGetProperty("placements", out var arr) && arr.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in arr.EnumerateArray())
                {
                    requests.Add(new PlacementRequest
                    {
                        ViewId = Args.RequireLong(item, "viewId"),
                        SheetId = Args.RequireLong(item, "sheetId"),
                        XMm = Args.GetDoubleOrNull(item, "xMm"),
                        YMm = Args.GetDoubleOrNull(item, "yMm")
                    });
                }
            }

            if (requests.Count == 0)
                return JsonSerializer.Serialize(new { error = "No placements supplied." });

            var succeeded = new List<object>();
            var failed = new List<object>();

            using (var tx = new Transaction(doc, "Place views on sheets"))
            {
                tx.Start();

                foreach (var p in requests)
                {
                    try
                    {
                        var sheet = doc.GetElement(new ElementId(p.SheetId)) as ViewSheet;

                        if (sheet == null)
                        {
                            failed.Add(new { viewId = p.ViewId, sheetId = p.SheetId, reason = "Sheet not found." });
                            continue;
                        }

                        var view = doc.GetElement(new ElementId(p.ViewId)) as View;

                        if (view == null)
                        {
                            failed.Add(new { viewId = p.ViewId, sheetId = p.SheetId, reason = "View not found." });
                            continue;
                        }

                        XYZ point = PlacementPoint(sheet, p);

                        // Schedules go on sheets through a different API from other views,
                        // and unlike other views they may appear on several sheets.
                        if (view is ViewSchedule schedule)
                        {
                            if (schedule.IsTitleblockRevisionSchedule)
                            {
                                failed.Add(new { viewId = p.ViewId, sheetId = p.SheetId, reason = "Title block revision schedules cannot be placed." });
                                continue;
                            }

                            var instance = ScheduleSheetInstance.Create(doc, sheet.Id, view.Id, point);

                            succeeded.Add(new
                            {
                                viewId = p.ViewId,
                                viewName = view.Name,
                                sheetNumber = sheet.SheetNumber,
                                kind = "schedule",
                                placedId = instance.Id.Value
                            });
                            continue;
                        }

                        if (!Viewport.CanAddViewToSheet(doc, sheet.Id, view.Id))
                        {
                            failed.Add(new
                            {
                                viewId = p.ViewId,
                                viewName = view.Name,
                                sheetId = p.SheetId,
                                reason = WhyNot(doc, view)
                            });
                            continue;
                        }

                        var viewport = Viewport.Create(doc, sheet.Id, view.Id, point);

                        succeeded.Add(new
                        {
                            viewId = p.ViewId,
                            viewName = view.Name,
                            sheetNumber = sheet.SheetNumber,
                            kind = "viewport",
                            placedId = viewport.Id.Value
                        });
                    }
                    catch (Exception ex)
                    {
                        failed.Add(new { viewId = p.ViewId, sheetId = p.SheetId, reason = ex.Message });
                    }
                }

                if (succeeded.Count > 0)
                    tx.Commit();
                else
                    tx.RollBack();
            }

            return JsonSerializer.Serialize(new
            {
                placed = succeeded.Count,
                failedCount = failed.Count,
                succeeded = succeeded,
                failed = failed
            });
        }

        // Position in millimetres from the sheet origin if given, otherwise the centre of the sheet.
        private static XYZ PlacementPoint(ViewSheet sheet, PlacementRequest p)
        {
            if (p.XMm.HasValue && p.YMm.HasValue)
            {
                return new XYZ(
                    UnitConvert.FromMm(p.XMm.Value),
                    UnitConvert.FromMm(p.YMm.Value),
                    0);
            }

            var outline = sheet.Outline;

            return new XYZ(
                (outline.Min.U + outline.Max.U) / 2,
                (outline.Min.V + outline.Max.V) / 2,
                0);
        }

        private static string WhyNot(Document doc, View view)
        {
            var existing = new FilteredElementCollector(doc)
                .OfClass(typeof(Viewport))
                .Cast<Viewport>()
                .FirstOrDefault(vp => vp.ViewId == view.Id);

            if (existing != null)
            {
                var onSheet = doc.GetElement(existing.SheetId) as ViewSheet;

                if (onSheet != null)
                {
                    return $"Already placed on sheet {onSheet.SheetNumber} - {onSheet.Name}. " +
                           "A view can only be on one sheet; duplicate the view first.";
                }
            }

            if (view.IsTemplate)
                return "View templates cannot be placed on sheets.";

            if (view is ViewSheet)
                return "A sheet cannot be placed on another sheet.";

            return "Revit does not allow this view to be placed on that sheet.";
        }
    }
}
