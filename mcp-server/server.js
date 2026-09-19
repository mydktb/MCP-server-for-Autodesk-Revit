import fs from "node:fs";
import net from "node:net";
import { z } from "zod";

const LOG = "C:\\Temp\\mcp-node.log";

function log(msg) {
  try {
    fs.mkdirSync("C:\\Temp", { recursive: true });
    fs.appendFileSync(LOG, `${new Date().toISOString()}  ${msg}\n`);
  } catch {}
}

process.on("uncaughtException", (err) => {
  log(`UNCAUGHT: ${err.stack || err}`);
  process.exit(1);
});

process.on("unhandledRejection", (err) => {
  log(`UNHANDLED REJECTION: ${err?.stack || err}`);
  process.exit(1);
});

process.on("exit", (code) => log(`Process exiting with code ${code}`));

log("=== starting ===");
log(`node ${process.version}`);

const { McpServer } = await import("@modelcontextprotocol/sdk/server/mcp.js");
const { StdioServerTransport } = await import("@modelcontextprotocol/sdk/server/stdio.js");
log("SDK imported OK");

const REVIT_HOST = "127.0.0.1";
const REVIT_PORT = 5566;

function callRevit(tool, args = {}) {
  return new Promise((resolve, reject) => {
    const socket = net.createConnection(REVIT_PORT, REVIT_HOST);
    let buffer = "";

    socket.setTimeout(30000);

    socket.on("timeout", () => {
      socket.destroy();
      reject(new Error("Revit did not respond within 30 seconds."));
    });

    socket.on("error", (err) => {
      reject(new Error(`Cannot reach Revit on ${REVIT_HOST}:${REVIT_PORT} — is the model open and the server started? (${err.message})`));
    });

    socket.on("connect", () => {
      socket.write(JSON.stringify({ tool, ...args }) + "\n");
    });

    socket.on("data", (chunk) => {
      buffer += chunk.toString();
      const nl = buffer.indexOf("\n");
      if (nl !== -1) {
        const line = buffer.slice(0, nl);
        socket.end();
        resolve(line);
      }
    });
  });
}

const server = new McpServer({ name: "mkrevit", version: "1.0.0" });
log("McpServer constructed");

server.registerTool(
  "get_model_info",
  {
    title: "Get Revit model info",
    description: "Get the title, file path, active view and view count of the Revit model currently open.",
    inputSchema: {},
  },
  async () => {
    log("get_model_info called");
    try {
      const result = await callRevit("get_model_info");
      log(`get_model_info result: ${result}`);
      return { content: [{ type: "text", text: result }] };
    } catch (err) {
      log(`get_model_info failed: ${err.message}`);
      return { content: [{ type: "text", text: `Error: ${err.message}` }], isError: true };
    }
  }
);

server.registerTool(
  "list_views",
  {
    title: "List Revit views",
    description: "List views in the open Revit model. Optionally filter by view type (e.g. FloorPlan, CeilingPlan, Section, ThreeD) or by text contained in the view name. Returns id, name and type for each.",
    inputSchema: {
      viewType: z.string().optional().describe("Filter by ViewType, e.g. FloorPlan"),
      nameContains: z.string().optional().describe("Only views whose name contains this text"),
    },
  },
  async ({ viewType, nameContains }) => {
    log(`list_views called: type=${viewType} contains=${nameContains}`);
    try {
      const result = await callRevit("list_views", { viewType, nameContains });
      log(`list_views result length: ${result.length}`);
      return { content: [{ type: "text", text: result }] };
    } catch (err) {
      log(`list_views failed: ${err.message}`);
      return { content: [{ type: "text", text: `Error: ${err.message}` }], isError: true };
    }
  }
);

server.registerTool(
  "rename_views",
  {
    title: "Rename Revit views",
    description: "Rename one or more views in the open Revit model. Supply an array of {id, newName}. Get ids from list_views first. View names must be unique within a view type.",
    inputSchema: {
      renames: z.array(
        z.object({
          id: z.number().describe("The view's element id"),
          newName: z.string().describe("The new view name"),
        })
      ).describe("The views to rename"),
    },
  },
  async ({ renames }) => {
    log(`rename_views called with ${renames.length} renames`);
    try {
      const result = await callRevit("rename_views", { renames });
      log(`rename_views result: ${result}`);
      return { content: [{ type: "text", text: result }] };
    } catch (err) {
      log(`rename_views failed: ${err.message}`);
      return { content: [{ type: "text", text: `Error: ${err.message}` }], isError: true };
    }
  }
);

log("tools registered");

const transport = new StdioServerTransport();
await server.connect(transport);
log("connected to transport — waiting for messages");