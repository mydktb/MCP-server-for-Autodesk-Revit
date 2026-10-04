using System;
using System.Collections.Generic;
using System.Text.Json;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace MKRevitMCP.McpTools
{
    public class ApplyViewTemplateTool : ITool
    {
        public string Name => "apply_view_template";

        public string Execute(UIApplication app, JsonElement args)
        {
            var doc = app.ActiveUIDocument?.Document;

            if (doc == null)
                return JsonSerializer.Serialize(new { error = "No document is open." });

            var viewIds = Args.GetLongList(args, "viewIds");

            if (viewIds.Count == 0)
                return JsonSerializer.Serialize(new { error = "No view ids supplied." });

            bool dryRun = Args.GetBool(args, "dryRun", true);

            // No templateId (or -1) means: remove the template from these views.
            long? requested = Args.GetLongOrNull(args, "templateId");
            bool removing = !requested.HasValue || requested.Value == -1;
            View template = null;

            if (!removing)
            {
                template = doc.GetElement(new ElementId(requested.Value)) as View;

                if (template == null || !template.IsTemplate)
                {
                    return JsonSerializer.Serialize(new
                    {
                        error = $"{requested.Value} is not a view template. Use list_view_templates."
                    });
                }
            }

            var plannedViews = new List<View>();
            var planned = new List<object>();
            var blocked = new List<object>();

            foreach (var rawId in viewIds)
            {
                var view = doc.GetElement(new ElementId(rawId)) as View;

                if (view == null)
                {
                    blocked.Add(new { id = rawId, reason = "Not a view, or id not found." });
                    continue;
                }

                if (view.IsTemplate)
                {
                    blocked.Add(new { id = rawId, name = view.Name, reason = "This is itself a template." });
                    continue;
                }

                if (!removing && !view.IsValidViewTemplate(template.Id))
                {
                    blocked.Add(new
                    {
                        id = rawId,
                        name = view.Name,
                        reason = $"'{template.Name}' is not valid for a {view.ViewType} view."
                    });
                    continue;
                }

                string current = view.ViewTemplateId == ElementId.InvalidElementId
                    ? null
                    : doc.GetElement(view.ViewTemplateId)?.Name;

                plannedViews.Add(view);
                planned.Add(new { id = rawId, name = view.Name, currentTemplate = current });
            }

            string target = removing ? "(no template)" : template.Name;

            if (dryRun)
            {
                return JsonSerializer.Serialize(new
                {
                    dryRun = true,
                    template = target,
                    wouldChange = planned.Count,
                    blockedCount = blocked.Count,
                    planned = planned,
                    blocked = blocked
                });
            }

            var failed = new List<object>(blocked);
            var changedViews = new List<object>();

            using (var tx = new Transaction(doc, removing ? "Remove view template" : "Apply view template"))
            {
                tx.Start();

                foreach (var view in plannedViews)
                {
                    try
                    {
                        view.ViewTemplateId = removing ? ElementId.InvalidElementId : template.Id;
                        changedViews.Add(new { id = view.Id.Value, name = view.Name });
                    }
                    catch (Exception ex)
                    {
                        failed.Add(new { id = view.Id.Value, name = view.Name, reason = ex.Message });
                    }
                }

                if (changedViews.Count > 0)
                    tx.Commit();
                else
                    tx.RollBack();
            }

            return JsonSerializer.Serialize(new
            {
                dryRun = false,
                template = target,
                changed = changedViews.Count,
                failedCount = failed.Count,
                changedViews = changedViews,
                failed = failed
            });
        }
    }
}
