using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace MKRevitMCP.McpTools
{
    public class FindUnusedFamiliesTool : ITool
    {
        public string Name => "find_unused_families";

        public string Execute(UIApplication app, JsonElement args)
        {
            var doc = app.ActiveUIDocument?.Document;

            if (doc == null)
                return JsonSerializer.Serialize(new { error = "No document is open." });

            string categoryContains = Args.GetString(args, "categoryContains");
            int maxFamilies = Args.GetInt(args, "maxFamilies", 100);
            if (maxFamilies <= 0) maxFamilies = 100;

            // Every type referenced by any placed element, not just family instances.
            // That catches tags, title blocks and other annotation, which aren't
            // FamilyInstances but still use a family type.
            var usedTypeIds = new HashSet<long>(
                new FilteredElementCollector(doc)
                    .WhereElementIsNotElementType()
                    .Select(e => e.GetTypeId())
                    .Where(t => t != null && t != ElementId.InvalidElementId)
                    .Select(t => t.Value));

            var families = new FilteredElementCollector(doc)
                .OfClass(typeof(Family))
                .Cast<Family>()
                .Where(f => !f.IsInPlace);

            if (!string.IsNullOrWhiteSpace(categoryContains))
            {
                families = families.Where(f =>
                    (f.FamilyCategory?.Name ?? "").IndexOf(categoryContains, StringComparison.OrdinalIgnoreCase) >= 0);
            }

            var unusedFamilies = new List<object>();
            var partlyUsed = new List<object>();
            int totalUnusedTypes = 0;

            foreach (var family in families.OrderBy(f => f.FamilyCategory?.Name).ThenBy(f => f.Name))
            {
                var types = family.GetFamilySymbolIds()
                    .Select(id => doc.GetElement(id))
                    .Where(t => t != null)
                    .ToList();

                if (types.Count == 0) continue;

                var unusedTypes = types.Where(t => !usedTypeIds.Contains(t.Id.Value)).ToList();
                if (unusedTypes.Count == 0) continue;

                totalUnusedTypes += unusedTypes.Count;
                string category = family.FamilyCategory?.Name ?? "(no category)";

                if (unusedTypes.Count == types.Count)
                {
                    unusedFamilies.Add(new
                    {
                        familyId = family.Id.Value,
                        name = family.Name,
                        category = category,
                        typeCount = types.Count
                    });
                }
                else
                {
                    partlyUsed.Add(new
                    {
                        familyId = family.Id.Value,
                        name = family.Name,
                        category = category,
                        usedTypes = types.Count - unusedTypes.Count,
                        unusedTypes = unusedTypes
                            .OrderBy(t => t.Name)
                            .Select(t => new { id = t.Id.Value, name = t.Name })
                            .ToList()
                    });
                }
            }

            return JsonSerializer.Serialize(new
            {
                unusedFamilyCount = unusedFamilies.Count,
                partlyUsedFamilyCount = partlyUsed.Count,
                totalUnusedTypes = totalUnusedTypes,
                truncated = unusedFamilies.Count > maxFamilies || partlyUsed.Count > maxFamilies,
                note = "A type can be in use without a placed element, e.g. as a default tag, a profile, or nested in another family. Treat these as candidates and preview with delete_elements before deleting.",
                unusedFamilies = unusedFamilies.Take(maxFamilies).ToList(),
                partlyUsedFamilies = partlyUsed.Take(maxFamilies).ToList()
            });
        }
    }
}
