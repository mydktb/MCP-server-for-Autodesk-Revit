using System;
using System.Collections.Generic;
using System.Text.Json;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace MKRevitMCP.McpTools
{
    public class GetScheduleDataTool : ITool
    {
        public string Name => "get_schedule_data";

        public string Execute(UIApplication app, JsonElement args)
        {
            var doc = app.ActiveUIDocument?.Document;

            if (doc == null)
                return JsonSerializer.Serialize(new { error = "No document is open." });

            long scheduleId = Args.RequireLong(args, "scheduleId");
            int maxRows = Args.GetInt(args, "maxRows", 200);
            if (maxRows <= 0) maxRows = 200;

            var schedule = doc.GetElement(new ElementId(scheduleId)) as ViewSchedule;

            if (schedule == null)
            {
                return JsonSerializer.Serialize(new
                {
                    error = $"{scheduleId} is not a schedule. Find schedules with list_views and viewType Schedule."
                });
            }

            // Read the table exactly as Revit displays it, column headings included.
            var body = schedule.GetTableData().GetSectionData(SectionType.Body);
            int totalRows = body.NumberOfRows;
            int columns = body.NumberOfColumns;
            int take = Math.Min(totalRows, maxRows);

            var rows = new List<List<string>>();

            for (int r = 0; r < take; r++)
            {
                var row = new List<string>();

                for (int c = 0; c < columns; c++)
                    row.Add(schedule.GetCellText(SectionType.Body, r, c));

                rows.Add(row);
            }

            return JsonSerializer.Serialize(new
            {
                id = scheduleId,
                name = schedule.Name,
                columns = columns,
                totalRows = totalRows,
                returnedRows = take,
                truncated = totalRows > take,
                rows = rows
            });
        }
    }
}
