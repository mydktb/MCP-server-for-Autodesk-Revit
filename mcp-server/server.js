import fs from "node:fs";
import net from "node:net";
import path from "node:path";
import { fileURLToPath } from "node:url";

// ---------- logging (stdout is the MCP channel, so never console.log) ----------

const LOG = process.platform === "win32" ? "C:\\Temp\\mcp-node.log" : "/tmp/mcp-node.log";

function log(msg) {
  try {
    fs.mkdirSync(path.dirname(LOG), { recursive: true });
    fs.appendFileSync(LOG, `${new Date().toISOString()}  [${process.pid}]  ${msg}\n`);
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

// ---------- load tool definitions ----------
// Resolved relative to this file, because Claude Desktop does not start us in this folder.

const here = path.dirname(fileURLToPath(import.meta.url));
const toolsPath = path.join(here, "tools.json");

let TOOLS;
try {
  TOOLS = JSON.parse(fs.readFileSync(toolsPath, "utf8"));
  log(`Loaded ${TOOLS.length} tools from ${toolsPath}`);
} catch (err) {
  log(`FAILED to load tools.json: ${err.message}`);
  process.exit(1);
}

const TOOL_NAMES = new Set(TOOLS.map((t) => t.name));

// ---------- talk to Revit ----------

const REVIT_HOST = "127.0.0.1";
const REVIT_PORT = 5566;

function callRevit(tool, args = {}) {
  return new Promise((resolve, reject) => {
    const socket = net.createConnection(REVIT_PORT, REVIT_HOST);
    let buffer = "";

    socket.setTimeout(30000);

    socket.on("timeout", () => {
      socket.destroy();
      reject(new Error("Revit did not respond within 30 seconds. Is a dialog open in Revit?"));
    });

    socket.on("error", (err) => {
      reject(new Error(`Cannot reach Revit on ${REVIT_HOST}:${REVIT_PORT} - is the model open and the server started? (${err.message})`));
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

// ---------- MCP server ----------

const { Server } = await import("@modelcontextprotocol/sdk/server/index.js");
const { StdioServerTransport } = await import("@modelcontextprotocol/sdk/server/stdio.js");
const { ListToolsRequestSchema, CallToolRequestSchema } = await import("@modelcontextprotocol/sdk/types.js");

const server = new Server(
  { name: "mkrevit", version: "2.0.0" },
  { capabilities: { tools: {} } }
);

server.setRequestHandler(ListToolsRequestSchema, async () => {
  log("tools/list requested");
  return { tools: TOOLS };
});

server.setRequestHandler(CallToolRequestSchema, async (request) => {
  const name = request.params.name;
  const args = request.params.arguments || {};

  log(`call ${name} ${JSON.stringify(args)}`);

  if (!TOOL_NAMES.has(name)) {
    return { content: [{ type: "text", text: `Unknown tool: ${name}` }], isError: true };
  }

  try {
    const result = await callRevit(name, args);
    log(`result ${name}: ${result.length} chars`);
    return { content: [{ type: "text", text: result }] };
  } catch (err) {
    log(`failed ${name}: ${err.message}`);
    return { content: [{ type: "text", text: `Error: ${err.message}` }], isError: true };
  }
});

const transport = new StdioServerTransport();
await server.connect(transport);
log("connected - waiting for messages");
