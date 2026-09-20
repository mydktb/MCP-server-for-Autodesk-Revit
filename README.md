# MKRevitMCP

An MCP server for Autodesk Revit. Lets Claude read from and write to an open Revit model.

## How it works

Two halves:

- **MKRevitMCP/** - a Revit add-in (C#, .NET 8) that listens on TCP port 5566 and marshals Revit API calls onto Revit's API thread via ExternalEvent.
- **mcp-server/** - a Node MCP server that Claude Desktop launches over stdio and which forwards requests to the add-in.

Requests travel: Claude Desktop -> stdio -> Node server -> TCP 5566 -> Revit add-in -> ExternalEvent -> Revit API thread, and the response comes back the same way.

## Requirements

- Revit 2025
- Visual Studio 2022
- Node.js 20+

## Setup

**1. Build the C# project** in Visual Studio. A post-build step copies the DLL and `.addin` manifest to `%AppData%\Autodesk\Revit\Addins\2025`.

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

**4. Restart Claude Desktop**, start Revit, open a model, and click **Start Server** on the MK Revit MCP ribbon tab. The button label toggles to **Stop Server** while the listener is running.

## Tools

### Model

| Tool | Description |
|---|---|
| `get_model_info` | Title, file path, active view, view count |
| `get_warnings` | Model warnings grouped by description, with counts and failing element ids |

### Views

| Tool | Description |
|---|---|
| `list_views` | List views, filtered by view type or name substring |
| `rename_views` | Batch rename views by element id |

### Elements

| Tool | Description |
|---|---|
| `get_categories_by_keywords` | Find category ids by keyword — call this first, category ids cannot be guessed |
| `count_elements_by_category` | Cheap instance counts for one or more categories |
| `get_elements_by_category` | Elements in a category, grouped by family and type |
| `set_selection` | Select elements by id in Revit and scroll the active view to them |

### Parameters

| Tool | Description |
|---|---|
| `get_element_parameters` | Every parameter on one element — use to discover exact names |
| `get_parameter_values` | Read named parameters across many elements, grouped by value |
| `set_parameter_value` | Write one instance parameter across many elements, dry run by default |

## Design notes

**Discovery before action.** Category ids and parameter names can't be guessed, so `get_categories_by_keywords` and `get_element_parameters` exist to be called first.

**Grouped returns.** Bulk tools group by type or value rather than returning flat rows, which keeps responses small on large models.

**Dry run by default.** `set_parameter_value` previews changes unless called with `dryRun: false`.

**Transactions.** All writes run inside a named Revit transaction, so they appear in the undo stack and roll back cleanly on failure.

**Units.** Revit stores doubles in internal units (feet). Parameter reads return both the raw value and a formatted `display` string; writes expect internal units.

## Notes

Revit only processes external events when it is idle, so requests queue if a modal dialog is open or a command is running.

Logs are written to `C:\Temp\mcp.log` (Revit side) and `C:\Temp\mcp-node.log` (Node side).

## Roadmap

- Ribbon icons and additional commands
- Rooms and areas
- Sheets and view placement
- Schedule creation