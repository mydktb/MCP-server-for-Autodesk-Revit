using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace MKRevitMCP.McpTools
{
    public class ListSchedulableFieldsTool : ITool
    {
        public string Name => "list_schedulable_fields";

        public string Execute(UIApplication app, JsonElement args)
        {
            var doc = app.ActiveUIDocument?.Document;

            if (doc == null)
                return JsonSerializer.Serialize(new { error = "No document is open." });

            long categoryId = Args.RequireLong(args, "categoryId");
            string contains = Args.GetString(args, "nameContains");

            var found = new List<(string FieldName, string FieldType)>();

            // Revit only reports schedulable fields from an actual schedule, so create a
            // temporary one, read its fields, and roll it back. Nothing is kept or added to undo.
            using (var tx = new Transaction(doc, "Probe schedulable fields"))
            {
                tx.Start();

                try
                {
                    var probe = ViewSchedule.CreateSchedule(doc, new ElementId(categoryId));

                    foreach (var f in probe.Definition.GetSchedulableFields())
                        found.Add((f.GetName(doc), f.FieldType.ToString()));
                }
                catch (Exception ex)
                {
                    if (tx.GetStatus() == TransactionStatus.Started)
                        tx.RollBack();

                    return JsonSerializer.Serialize(new
                    {
                        error = "This category cannot be scheduled: " + ex.Message
                    });
                }

                tx.RollBack();
            }

            var fields = found
                .Where(f => string.IsNullOrWhiteSpace(contains) ||
                            f.FieldName.IndexOf(contains, StringComparison.OrdinalIgnoreCase) >= 0)
                .OrderBy(f => f.FieldName)
                .Select(f => new { name = f.FieldName, fieldType = f.FieldType })
                .ToList();

            string categoryName = Category.GetCategory(doc, new ElementId(categoryId))?.Name;

            return JsonSerializer.Serialize(new
            {
                categoryId = categoryId,
                category = categoryName,
                count = fields.Count,
                fields = fields
            });
        }
    }
}
