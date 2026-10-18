using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace MKRevitMCP.McpTools
{
    public class DeleteElementsTool : ITool
    {
        public string Name => "delete_elements";

        public string Execute(UIApplication app, JsonElement args)
        {
            var doc = app.ActiveUIDocument?.Document;

            if (doc == null)
                return JsonSerializer.Serialize(new { error = "No document is open." });

            var requestedIds = Args.GetLongList(args, "elementIds");
            bool dryRun = Args.GetBool(args, "dryRun", true);
            bool includePinned = Args.GetBool(args, "includePinned", false);

            if (requestedIds.Count == 0)
                return JsonSerializer.Serialize(new { error = "No element ids supplied." });

            var toDelete = new List<ElementId>();
            var blocked = new List<object>();
            var activeViewId = doc.ActiveView?.Id;

            foreach (var raw in requestedIds.Distinct())
            {
                var id = new ElementId(raw);
                var element = doc.GetElement(id);

                if (element == null)
                {
                    blocked.Add(new { id = raw, reason = "Element not found." });
                    continue;
                }

                if (activeViewId != null && id == activeViewId)
                {
                    blocked.Add(new { id = raw, name = element.Name, reason = "This is the active view. Switch to another view first." });
                    continue;
                }

                if (element.Pinned && !includePinned)
                {
                    blocked.Add(new { id = raw, name = element.Name, reason = "Pinned. Pass includePinned true to delete it anyway." });
                    continue;
                }

                toDelete.Add(id);
            }

            if (toDelete.Count == 0)
            {
                return JsonSerializer.Serialize(new
                {
                    dryRun = dryRun,
                    deleted = 0,
                    blockedCount = blocked.Count,
                    blocked = blocked
                });
            }

            // Deleting one element can take others with it: a level takes its views,
            // a family takes its types and instances. The only reliable way to see the
            // full cascade is to delete for real inside a transaction, record what went,
            // and roll back. Nothing is kept and nothing reaches the undo list.
            ICollection<ElementId> cascade;

            using (var probe = new Transaction(doc, "Preview delete"))
            {
                probe.Start();
                SilentFailures.Apply(probe);

                try
                {
                    cascade = doc.Delete(toDelete);
                }
                catch (Exception ex)
                {
                    probe.RollBack();
                    return JsonSerializer.Serialize(new
                    {
                        error = "Revit refused the deletion: " + ex.Message,
                        blocked = blocked
                    });
                }

                probe.RollBack();
            }

            // After the rollback every element exists again, so they can be described.
            var requestedSet = new HashSet<long>(toDelete.Select(i => i.Value));
            var summary = cascade
                .Select(id => doc.GetElement(id))
                .Where(e => e != null)
                .GroupBy(e => e.Category?.Name ?? "(no category)")
                .OrderByDescending(g => g.Count())
                .Select(g => new
                {
                    category = g.Key,
                    count = g.Count(),
                    requested = g.Count(e => requestedSet.Contains(e.Id.Value)),
                    alsoDeleted = g.Count(e => !requestedSet.Contains(e.Id.Value)),
                    sampleNames = g.Select(e => e.Name).Where(n => !string.IsNullOrWhiteSpace(n)).Distinct().Take(5).ToList()
                })
                .ToList();

            int cascadeExtra = cascade.Count - toDelete.Count;

            if (dryRun)
            {
                return JsonSerializer.Serialize(new
                {
                    dryRun = true,
                    requested = toDelete.Count,
                    totalThatWouldBeDeleted = cascade.Count,
                    alsoDeletedByCascade = cascadeExtra,
                    byCategory = summary,
                    blockedCount = blocked.Count,
                    blocked = blocked
                });
            }

            using (var tx = new Transaction(doc, "Delete elements"))
            {
                tx.Start();
                SilentFailures.Apply(tx);

                try
                {
                    var deleted = doc.Delete(toDelete);
                    var status = tx.Commit();

                    if (status != TransactionStatus.Committed)
                    {
                        return JsonSerializer.Serialize(new
                        {
                            error = "Revit rolled the deletion back because of errors. Nothing was deleted.",
                            blocked = blocked
                        });
                    }

                    return JsonSerializer.Serialize(new
                    {
                        dryRun = false,
                        deleted = deleted.Count,
                        alsoDeletedByCascade = deleted.Count - toDelete.Count,
                        byCategory = summary,
                        blockedCount = blocked.Count,
                        blocked = blocked
                    });
                }
                catch (Exception ex)
                {
                    if (tx.GetStatus() == TransactionStatus.Started)
                        tx.RollBack();

                    return JsonSerializer.Serialize(new { error = "Nothing was deleted: " + ex.Message });
                }
            }
        }
    }
}
