# MKRevitMCP

An MCP server for Autodesk Revit. Lets Claude read from and write to an open Revit model: query elements and parameters, audit warnings, rename views, create sheets and place views on them.

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

### Sheets

| Tool | Description |
|---|---|
| `list_sheets` | Sheets with number, name and the views placed on each |
| `list_titleblocks` | Title block types and their ids |
| `create_sheets` | Create sheets with a number, name and title block |
| `rename_sheets` | Renumber and/or rename sheets; swapping numbers is supported |
| `place_views_on_sheets` | Place views or schedules on sheets, positioned in mm or centred |

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

Build, restart Claude Desktop, and it's live. The name must match exactly in both places. If it doesn't, Claude sees the tool but Revit answers `Unknown tool`.

## Design notes

**Discovery before action.** Category ids, parameter names and title block ids can't be guessed, so each has a lookup tool that Claude calls first.

**Grouped returns.** Bulk tools group results by type or value instead of returning one row per element. This keeps responses small on large models.

**Dry run by default.** `set_parameter_value` previews its changes unless called with `dryRun: false`.

**Transactions.** Every write runs inside a named Revit transaction. Changes appear in the undo stack as one step and roll back cleanly on failure.

**Units.** Revit stores all lengths internally in feet. Tools take and return millimetres, and `UnitConvert` handles the conversion at the boundary. Parameter reads return both the raw internal value and a formatted `display` string.

**Saving.** Tools change the open model but never save it. Save in Revit to keep changes.

## Troubleshooting

Revit only processes requests when it is idle. If a dialog is open or a command is running, requests queue and may time out.

Two log files record what happened:

- `C:\Temp\mcp.log` - Revit side: tool registration, each request, results
- `C:\Temp\mcp-node.log` - Node side: startup, tool list, each call

Between them you can see how far a request got before it stopped.

## Roadmap

- Views: create floor plans, duplicate views, apply view templates
- Schedules
- Cleanup: delete elements, unplaced views, unused families
- Levels and geometry
- More ribbon commands and icons
