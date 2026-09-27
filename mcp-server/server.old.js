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

server.registerTool(
  "get_warnings",
  {
    title: "Get Revit model warnings",
    description: "List the warnings in the open Revit model, grouped by description with counts and the element ids involved. Use this to audit model health.",
    inputSchema: {
      maxIdsPerGroup: z.number().optional().describe("Max element ids returned per warning group, default 20"),
    },
  },
  async ({ maxIdsPerGroup }) => {
    log(`get_warnings called`);
    try {
      const result = await callRevit("get_warnings", { maxIdsPerGroup });
      return { content: [{ type: "text", text: result }] };
    } catch (err) {
      return { content: [{ type: "text", text: `Error: ${err.message}` }], isError: true };
    }
  }
);

server.registerTool(
  "set_selection",
  {
    title: "Select elements in Revit",
    description: "Select elements in the open Revit model by element id, and scroll the active view to show them. Pass an empty array to clear the selection.",
    inputSchema: {
      ids: z.array(z.number()).describe("Element ids to select"),
    },
  },
  async ({ ids }) => {
    log(`set_selection called with ${ids.length} ids`);
    try {
      const result = await callRevit("set_selection", { ids });
      return { content: [{ type: "text", text: result }] };
    } catch (err) {
      return { content: [{ type: "text", text: `Error: ${err.message}` }], isError: true };
    }
  }
);

server.registerTool(
  "get_categories_by_keywords",
  {
    title: "Find Revit categories",
    description: "Find Revit categories whose name contains any of the given keywords. Returns the category id needed by the other category tools. Always call this first — category ids are negative numbers that cannot be guessed.",
    inputSchema: {
      keywords: z.array(z.string()).describe("Words to match against category names, e.g. ['Door','Window']"),
    },
  },
  async ({ keywords }) => {
    log(`get_categories_by_keywords: ${keywords.join(", ")}`);
    try {
      const result = await callRevit("get_categories_by_keywords", { keywords });
      return { content: [{ type: "text", text: result }] };
    } catch (err) {
      return { content: [{ type: "text", text: `Error: ${err.message}` }], isError: true };
    }
  }
);

server.registerTool(
  "count_elements_by_category",
  {
    title: "Count elements by category",
    description: "Count placed element instances in one or more categories. Cheap — use this before get_elements_by_category to see how big the result will be.",
    inputSchema: {
      categoryIds: z.array(z.number()).describe("Category ids from get_categories_by_keywords"),
    },
  },
  async ({ categoryIds }) => {
    log(`count_elements_by_category: ${categoryIds.join(", ")}`);
    try {
      const result = await callRevit("count_elements_by_category", { categoryIds });
      return { content: [{ type: "text", text: result }] };
    } catch (err) {
      return { content: [{ type: "text", text: `Error: ${err.message}` }], isError: true };
    }
  }
);

server.registerTool(
  "get_elements_by_category",
  {
    title: "Get elements in a category",
    description: "Get placed elements in a category, grouped by family and type with element ids. Ids can be passed to set_selection. Use maxIdsPerType to cap the response on large categories.",
    inputSchema: {
      categoryId: z.number().describe("Category id from get_categories_by_keywords"),
      maxIdsPerType: z.number().optional().describe("Max ids returned per type group, default 50"),
      groupByType: z.boolean().optional().describe("Group by family and type, default true"),
    },
  },
  async ({ categoryId, maxIdsPerType, groupByType }) => {
    log(`get_elements_by_category: ${categoryId}`);
    try {
      const result = await callRevit("get_elements_by_category", { categoryId, maxIdsPerType, groupByType });
      return { content: [{ type: "text", text: result }] };
    } catch (err) {
      return { content: [{ type: "text", text: `Error: ${err.message}` }], isError: true };
    }
  }
);

server.registerTool(
  "get_element_parameters",
  {
    title: "Get all parameters of one element",
    description: "List every parameter on a single element, with values, storage types and whether each is read-only. Use this first to discover exact parameter names before reading or writing across many elements. Double values are in Revit internal units (feet); the 'display' field shows the formatted value.",
    inputSchema: {
      elementId: z.number().describe("The element id to inspect"),
      includeType: z.boolean().optional().describe("Also return the element type's parameters, default true"),
    },
  },
  async ({ elementId, includeType }) => {
    log(`get_element_parameters: ${elementId}`);
    try {
      const result = await callRevit("get_element_parameters", { elementId, includeType });
      return { content: [{ type: "text", text: result }] };
    } catch (err) {
      return { content: [{ type: "text", text: `Error: ${err.message}` }], isError: true };
    }
  }
);

server.registerTool(
  "get_parameter_values",
  {
    title: "Read parameters across many elements",
    description: "Read named parameters across many elements, grouped by value by default so large sets stay compact. Falls back to the element type when an instance has no such parameter. Get exact names from get_element_parameters first.",
    inputSchema: {
      elementIds: z.array(z.number()).describe("Elements to read"),
      parameterNames: z.array(z.string()).describe("Exact parameter names, e.g. ['Mark','Fire Rating']"),
      groupByValue: z.boolean().optional().describe("Group ids by shared value, default true"),
    },
  },
  async ({ elementIds, parameterNames, groupByValue }) => {
    log(`get_parameter_values: ${elementIds.length} elements, ${parameterNames.join(", ")}`);
    try {
      const result = await callRevit("get_parameter_values", { elementIds, parameterNames, groupByValue });
      return { content: [{ type: "text", text: result }] };
    } catch (err) {
      return { content: [{ type: "text", text: `Error: ${err.message}` }], isError: true };
    }
  }
);

server.registerTool(
  "set_parameter_value",
  {
    title: "Set a parameter on many elements",
    description: "Write one instance parameter across many elements. Defaults to a dry run that reports what would change without changing it — call with dryRun false to commit. Only instance parameters are written, never type parameters. Double values must be supplied in Revit internal units (feet).",
    inputSchema: {
      elementIds: z.array(z.number()).describe("Elements to modify"),
      parameterName: z.string().describe("Exact parameter name"),
      newValue: z.string().describe("New value as text; converted to the parameter's storage type"),
      dryRun: z.boolean().optional().describe("Preview only, default true"),
    },
  },
  async ({ elementIds, parameterName, newValue, dryRun }) => {
    log(`set_parameter_value: ${parameterName}=${newValue} on ${elementIds.length} elements, dryRun=${dryRun !== false}`);
    try {
      const result = await callRevit("set_parameter_value", { elementIds, parameterName, newValue, dryRun });
      return { content: [{ type: "text", text: result }] };
    } catch (err) {
      return { content: [{ type: "text", text: `Error: ${err.message}` }], isError: true };
    }
  }
);

log("tools registered");

const transport = new StdioServerTransport();
await server.connect(transport);
log("connected to transport — waiting for messages");