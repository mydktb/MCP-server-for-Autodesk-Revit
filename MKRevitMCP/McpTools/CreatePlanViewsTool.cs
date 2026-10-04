using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace MKRevitMCP.McpTools
{
    public class CreatePlanViewsTool : ITool
    {
        public string Name => "create_plan_views";

        private class PlanRequest
        {
            public long LevelId;
            public string ViewName;
        }

        public string Execute(UIApplication app, JsonElement args)
        {
            var doc = app.ActiveUIDocument?.Document;

            if (doc == null)
                return JsonSerializer.Serialize(new { error = "No document is open." });

            var requests = new List<PlanRequest>();

            if (args.TryGetProperty("plans", out var arr) && arr.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in arr.EnumerateArray())
                {
                    string n = Args.GetString(item, "name");

                    requests.Add(new PlanRequest
                    {
                        LevelId = Args.RequireLong(item, "levelId"),
                        ViewName = string.IsNullOrWhiteSpace(n) ? null : n.Trim()
                    });
                }
            }

            if (requests.Count == 0)
                return JsonSerializer.Serialize(new { error = "No plans supplied." });

            // Which kind of plan.
            string planType = Args.GetString(args, "planType", "FloorPlan");
            ViewFamily family;

            switch (planType.Trim().ToLowerInvariant())
            {
                case "floorplan": family = ViewFamily.FloorPlan; break;
                case "ceilingplan": family = ViewFamily.CeilingPlan; break;
                case "structuralplan": family = ViewFamily.StructuralPlan; break;
                default:
                    return JsonSerializer.Serialize(new
                    {
                        error = $"Unknown planType '{planType}'. Use FloorPlan, CeilingPlan or StructuralPlan."
                    });
            }

            // The view family type: the one asked for, else the first of the right family.
            var familyTypes = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewFamilyType))
                .Cast<ViewFamilyType>()
                .Where(t => t.ViewFamily == family)
                .OrderBy(t => t.Name)
                .ToList();

            long? requestedType = Args.GetLongOrNull(args, "viewFamilyTypeId");
            ViewFamilyType viewFamilyType;

            if (requestedType.HasValue)
            {
                viewFamilyType = familyTypes.FirstOrDefault(t => t.Id.Value == requestedType.Value);

                if (viewFamilyType == null)
                {
                    return JsonSerializer.Serialize(new
                    {
                        error = $"{requestedType.Value} is not a {planType} view family type."
                    });
                }
            }
            else
            {
                viewFamilyType = familyTypes.FirstOrDefault();

                if (viewFamilyType == null)
                {
                    return JsonSerializer.Serialize(new
                    {
                        error = $"The model has no {planType} view family type."
                    });
                }
            }

            // Optional template to apply as each view is created.
            long? requestedTemplate = Args.GetLongOrNull(args, "templateId");
            View template = null;

            if (requestedTemplate.HasValue)
            {
                template = doc.GetElement(new ElementId(requestedTemplate.Value)) as View;

                if (template == null || !template.IsTemplate)
                {
                    return JsonSerializer.Serialize(new
                    {
                        error = $"{requestedTemplate.Value} is not a view template. Use list_view_templates."
                    });
                }
            }

            var succeeded = new List<object>();
            var failed = new List<object>();

            using (var tx = new Transaction(doc, "Create plan views"))
            {
                tx.Start();

                foreach (var r in requests)
                {
                    var level = doc.GetElement(new ElementId(r.LevelId)) as Level;

                    if (level == null)
                    {
                        failed.Add(new { levelId = r.LevelId, reason = "Not a level, or id not found." });
                        continue;
                    }

                    ViewPlan plan = null;

                    try
                    {
                        plan = ViewPlan.Create(doc, viewFamilyType.Id, level.Id);

                        if (r.ViewName != null)
                            plan.Name = r.ViewName;

                        string templateResult = null;

                        if (template != null)
                        {
                            if (plan.IsValidViewTemplate(template.Id))
                            {
                                plan.ViewTemplateId = template.Id;
                                templateResult = template.Name;
                            }
                            else
                            {
                                templateResult = $"not applied - '{template.Name}' is not valid for this view";
                            }
                        }

                        succeeded.Add(new
                        {
                            id = plan.Id.Value,
                            name = plan.Name,
                            level = level.Name,
                            template = templateResult
                        });
                    }
                    catch (Exception ex)
                    {
                        // Don't leave a half-configured view behind.
                        if (plan != null)
                        {
                            try { doc.Delete(plan.Id); } catch { }
                        }

                        failed.Add(new { levelId = r.LevelId, level = level.Name, reason = ex.Message });
                    }
                }

                if (succeeded.Count > 0)
                    tx.Commit();
                else
                    tx.RollBack();
            }

            return JsonSerializer.Serialize(new
            {
                created = succeeded.Count,
                failedCount = failed.Count,
                planType = family.ToString(),
                viewFamilyType = viewFamilyType.Name,
                succeeded = succeeded,
                failed = failed
            });
        }
    }
}
