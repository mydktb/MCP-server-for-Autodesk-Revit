# MKRevitMCP

An MCP server for Autodesk Revit. Lets Claude read from and write to an open Revit model.

## How it works

Two halves:

- **MKRevitMCP/** - a Revit add-in (C#, .NET 8) that listens on TCP port 5566 and marshals Revit API calls onto Revit's API thread via ExternalEvent.
- **mcp-server/** - a Node MCP server that Claude Desktop launches over stdio and which forwards requests to the add-in.

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

**4. Restart Claude Desktop**, start Revit, open a model, and click **Start Server** on the MK Revit MCP ribbon tab.

## Tools

| Tool | Description |
|---|---|
| `get_model_info` | Title, file path, active view, view count |
| `list_views` | List views, filtered by type or name |
| `rename_views` | Batch rename views by element id |

## Notes

Revit only processes external events when it is idle, so requests will queue if a modal dialog is open or a command is running.

Logs are written to `C:\Temp\mcp.log` (Revit side) and `C:\Temp\mcp-node.log` (Node side).