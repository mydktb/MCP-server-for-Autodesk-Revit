# MKRevitMCP

An MCP server for Autodesk Revit. Lets Claude read from and write to an open Revit model: query elements and parameters, audit warnings, create plans, sheets and schedules, and clean up unused content.

## How it works

Two halves:

- **MKRevitMCP/** - a Revit add-in (C#, .NET 8) that listens on TCP port 5566 and runs each request on Revit's API thread via `ExternalEvent`.
- **mcp-server/** - a Node MCP server that Claude Desktop launches over stdio. It reads its tool list from `tools.json` and forwards every call to the add-in unchanged.

A request travels: Claude Desktop -> stdio -> Node server -> TCP 5566 -> Revit add-in -> Revit API thread, and the response comes back the same way.

## Requirements

- Revit 2025
- Visual Studio 2022
- Node.js 20+

## Setup

**1. Build the C# project** in Visual Studio. A post-build step copies the DLL and `.addin` manifest to `%AppData%\Autodesk\Revit\Addins\2025`. Close Revit before building, or the copy fails on the locked DLL.

**2. Install the Node dependencies:**

```
cd mcp-server
npm install
```

**3. Add this to `claude_desktop_config.json`:**

```json
{
  "mcpServers": {
    "mkrevit": {
      "command": "C:\\Program Files\\nodejs\\node.exe",
      "args": ["C:\\path\\to\\MKRevitMCP\\mcp-server\\server.js"]
    }
  }
}
```

Use the full path to `node.exe`. Claude Desktop does not always inherit your PATH, so a bare `node` can fail to launch.

**4. Restart Claude Desktop**, start Revit, open a model, and click **Start** on the MK Revit MCP ribbon tab. The label switches to **Stop** while the server is running.

## Tools

### Model

| Tool | Description |
|---|---|
| `get_model_info` | Title, file path, active view, view count |
| `get_warnings` | Model warnings grouped by description, with counts and the element ids involved |

### Views

| Tool | Description |
|---|---|
| `list_views` | List views, filtered by view type or name |
| `rename_views` | Batch rename views by element id |
| `list_levels` | Levels with elevation in mm and the plan views on each |
| `list_view_templates` | View templates and the view type each applies to |
| `create_plan_views` | Floor, ceiling or structural plans per level, with optional name and template |
| `duplicate_views` | Duplicate views plainly, with detailing, or as dependents |
| `apply_view_template` | Apply or remove a template on many views. Dry run by default |

### Sheets

| Tool | Description |
|---|---|
| `list_sheets` | Sheets with number, name and the views placed on each |
| `list_titleblocks` | Title block types and their ids |
| `create_sheets` | Create sheets with a number, name and title block |
| `rename_sheets` | Renumber and/or rename sheets; swapping numbers is supported |
| `place_views_on_sheets` | Place views or schedules on sheets, positioned in mm or centred |

### Schedules

| Tool | Description |
|---|---|
| `list_schedulable_fields` | Field names available to a schedule of a given category |
| `create_schedule` | Create a schedule with fields, sorting, and itemized or grouped rows |
| `get_schedule_data` | Read a schedule's contents as rows, exactly as Revit displays them |

### Elements

| Tool | Description |
|---|---|
| `get_categories_by_keywords` | Find category ids by keyword. Call this first, since category ids can't be guessed |
| `count_elements_by_category` | Cheap instance counts for one or more categories |
| `get_elements_by_category` | Elements in a category, grouped by family and type |
| `set_selection` | Select elements in Revit and scroll the active view to them |

### Parameters

| Tool | Description |
|---|---|
| `get_element_parameters` | Every parameter on one element. Use it to discover exact names |
| `get_parameter_values` | Read named parameters across many elements, grouped by value |
| `set_parameter_value` | Write one instance parameter across many elements. Dry run by default |

### Cleanup

| Tool | Description |
|---|---|
| `find_unplaced_views` | Views and schedules not on any sheet, with dependent-view counts |
| `find_unused_families` | Families with no placed instances, and families with unused types |
| `delete_elements` | Delete elements. Dry run by default, showing the full cascade of what else would go |

## Adding a tool

Two steps. `McpServer.cs` and `server.js` never need editing.

**1. Add a class in `MKRevitMCP/McpTools/`** that implements `ITool`:

```csharp
public class MyNewTool : ITool
{
    public string Name => "my_new_tool";

    public string Execute(UIApplication app, JsonElement args)
    {
        string text = Args.GetString(args, "someArgument");
        // ... do the Revit work, return a JSON string
    }
}
```

The add-in finds it automatically at startup.

**2. Add an entry to `mcp-server/tools.json`** with the same name, a description, and a JSON Schema for its arguments.

Build with Revit closed, then quit and relaunch Claude Desktop. The name must match exactly in both places. If it doesn't, Claude sees the tool but Revit answers `Unknown tool`.

## Design notes

**Discovery before action.** Category ids, parameter names, level ids, template ids, title block ids and schedulable field names can't be guessed, so each has a lookup tool that Claude calls first.

**Grouped returns.** Bulk tools group results by type or value instead of returning one row per element. This keeps responses small on large models.

**Dry run by default.** `set_parameter_value`, `apply_view_template` and `delete_elements` preview their changes unless called with `dryRun: false`. The delete preview performs the deletion inside a transaction, records everything Revit removes, and rolls back, so it shows the real cascade rather than a guess.

**Transactions.** Every write runs inside a named Revit transaction. Changes appear in the undo stack as one step and roll back cleanly on failure.

**No blocking dialogs.** Revit can show warnings in a modal dialog when a transaction commits, which would freeze the server. `SilentFailures` dismisses warnings and rolls back on real errors instead.

**Units.** Revit stores all lengths internally in feet. Tools take and return millimetres, and `UnitConvert` handles the conversion at the boundary. Parameter reads return both the raw internal value and a formatted `display` string.

**Saving.** Tools change the open model but never save it. Save in Revit to keep changes.

## Troubleshooting

**Claude doesn't see a new tool.** Claude Desktop reads `tools.json` once, at startup. Quit it from the system tray (closing the window is not enough), relaunch, and start a new chat.

**Claude sees the tool but Revit says `Unknown tool`.** The add-in wasn't rebuilt, usually because Revit was open during the build. Close Revit and rebuild.

**Check both halves agree.** Each log reports how many tools it loaded:

```powershell
Select-String -Path C:\Temp\mcp-node.log -Pattern "Loaded" | Select-Object -Last 1
Select-String -Path C:\Temp\mcp.log -Pattern "ToolRegistry ready" | Select-Object -Last 1
```

The two counts should match.

**Requests time out.** Revit only processes requests when it is idle. If a dialog is open or a command is running, requests queue and may time out.

Two log files record what happened:

- `C:\Temp\mcp.log` - Revit side: tool registration, each request, results
- `C:\Temp\mcp-node.log` - Node side: startup, tool list, each call

Between them you can see how far a request got before it stopped.

## Roadmap

- Levels and basic geometry
- Distribution: installer, one-click Claude Desktop extension, Revit 2026 support
- Security token and configurable port
- Ribbon icons and additional commands
