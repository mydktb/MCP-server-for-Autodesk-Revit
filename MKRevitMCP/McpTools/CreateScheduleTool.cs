using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace MKRevitMCP.McpTools
{
    public class CreateScheduleTool : ITool
    {
        public string Name => "create_schedule";

        public string Execute(UIApplication app, JsonElement args)
        {
            var doc = app.ActiveUIDocument?.Document;

            if (doc == null)
                return JsonSerializer.Serialize(new { error = "No document is open." });

            long categoryId = Args.RequireLong(args, "categoryId");
            string scheduleName = Args.GetString(args, "name");
            var fieldNames = Args.GetStringList(args, "fields");
            var sortBy = Args.GetStringList(args, "sortBy");
            bool itemized = Args.GetBool(args, "itemized", true);

            if (fieldNames.Count == 0)
                return JsonSerializer.Serialize(new { error = "No fields supplied. Use list_schedulable_fields to find names." });

            using (var tx = new Transaction(doc, "Create schedule"))
            {
                tx.Start();

                try
                {
                    var schedule = ViewSchedule.CreateSchedule(doc, new ElementId(categoryId));

                    if (!string.IsNullOrWhiteSpace(scheduleName))
                        schedule.Name = scheduleName.Trim();

                    var definition = schedule.Definition;
                    var available = definition.GetSchedulableFields();

                    // Add fields in the order requested, matching names case-insensitively.
                    var added = new Dictionary<string, ScheduleField>(StringComparer.OrdinalIgnoreCase);
                    var addedOrder = new List<string>();
                    var unknown = new List<string>();

                    foreach (var requested in fieldNames)
                    {
                        if (added.ContainsKey(requested)) continue;

                        var match = available.FirstOrDefault(f =>
                            string.Equals(f.GetName(doc), requested, StringComparison.OrdinalIgnoreCase));

                        if (match == null)
                        {
                            unknown.Add(requested);
                            continue;
                        }

                        added[requested] = definition.AddField(match);
                        addedOrder.Add(match.GetName(doc));
                    }

                    if (added.Count == 0)
                    {
                        tx.RollBack();

                        return JsonSerializer.Serialize(new
                        {
                            error = "None of the requested fields exist for this category. Use list_schedulable_fields.",
                            unknownFields = unknown
                        });
                    }

                    // Sorting: only fields that were added can be sorted on.
                    var sortIgnored = new List<object>();

                    foreach (var s in sortBy)
                    {
                        if (!added.TryGetValue(s, out var field))
                        {
                            sortIgnored.Add(new { field = s, reason = "Not one of the schedule's fields." });
                            continue;
                        }

                        try
                        {
                            definition.AddSortGroupField(
                                new ScheduleSortGroupField(field.FieldId, ScheduleSortOrder.Ascending));
                        }
                        catch (Exception ex)
                        {
                            sortIgnored.Add(new { field = s, reason = ex.Message });
                        }
                    }

                    definition.IsItemized = itemized;

                    tx.Commit();

                    return JsonSerializer.Serialize(new
                    {
                        id = schedule.Id.Value,
                        name = schedule.Name,
                        category = Category.GetCategory(doc, new ElementId(categoryId))?.Name,
                        fields = addedOrder,
                        unknownFields = unknown,
                        sortIgnored = sortIgnored,
                        itemized = itemized
                    });
                }
                catch (Exception ex)
                {
                    if (tx.GetStatus() == TransactionStatus.Started)
                        tx.RollBack();

                    return JsonSerializer.Serialize(new { error = "Schedule not created: " + ex.Message });
                }
            }
        }
    }
}
