#!/usr/bin/env node
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { StdioServerTransport } from "@modelcontextprotocol/sdk/server/stdio.js";
import { z } from "zod";

const BASE = process.env.AUTONOME_API_URL || "http://127.0.0.1:3801";

// --- Helper: call the C# REST API and return MCP-formatted result ---

async function api(method, path, body, extraHeaders) {
  const opts = {
    method,
    headers: { "Content-Type": "application/json", ...extraHeaders },
  };
  if (body) opts.body = JSON.stringify(body);

  let res;
  try {
    res = await fetch(BASE + path, opts);
  } catch (e) {
    return {
      content: [{ type: "text", text: `Connection error: ${e.message}\nIs the C# server running? Start with: dotnet run --project src/Autonome.Web -- worlds/coastal_city` }],
      isError: true,
    };
  }

  const data = await res.json().catch(() => null);
  if (!res.ok) {
    return {
      content: [{ type: "text", text: `HTTP ${res.status}: ${data?.error || res.statusText}` }],
      isError: true,
    };
  }
  return { content: [{ type: "text", text: JSON.stringify(data, null, 2) }] };
}

// --- MCP Server ---

const server = new McpServer({
  name: "autonome-sim",
  version: "1.0.0",
});

// ===================== SIMULATION CONTROL =====================

server.tool(
  "sim_status",
  "Get current simulation status: tick number, game time, auto-advance state, entity count",
  {},
  async () => api("GET", "/api/simulation/status")
);

server.tool(
  "sim_tick",
  "Advance the simulation by one tick. Returns all action events that occurred.",
  {},
  async () => api("POST", "/api/simulation/tick")
);

server.tool(
  "sim_auto",
  "Start auto-advancing the simulation at the given ticks per second (0.5-20).",
  { ticksPerSecond: z.number().min(0.5).max(20).describe("Ticks per second") },
  async ({ ticksPerSecond }) => api("POST", "/api/simulation/auto", { ticksPerSecond })
);

server.tool(
  "sim_pause",
  "Pause auto-advancing the simulation.",
  {},
  async () => api("POST", "/api/simulation/pause")
);

// ===================== WORLD STATE =====================

server.tool(
  "world_state",
  "Get full world state: all locations with entity counts, properties, tags, and connections.",
  {},
  async () => api("GET", "/api/world/state")
);

server.tool(
  "world_entities",
  "List all entities in the simulation with their ID, display name, location, embodied status, and tags.",
  {},
  async () => api("GET", "/api/world/entities")
);

server.tool(
  "world_events",
  "Get recent action events from the last 10 ticks.",
  {},
  async () => api("GET", "/api/world/tick")
);

// ===================== ENTITY =====================

server.tool(
  "entity_state",
  "Get detailed state of a specific entity: properties (with values/ranges/decay), personality traits, location, active modifiers, and relationships.",
  { entityId: z.string().describe("Entity ID (e.g. 'npc_aldric_thresher')") },
  async ({ entityId }) => api("GET", `/api/entity/${encodeURIComponent(entityId)}/state`)
);

server.tool(
  "entity_actions",
  "Get all available actions for an entity, scored and sorted by utility. Shows action name, category, score, and effects.",
  { entityId: z.string().describe("Entity ID to get actions for") },
  async ({ entityId }) => api("GET", `/api/entity/${encodeURIComponent(entityId)}/actions`)
);

server.tool(
  "entity_act",
  "Submit an action for a possessed entity. Requires the bearer token from entity_possess. The action will execute on the next tick.",
  {
    entityId: z.string().describe("Entity ID to act as"),
    actionId: z.string().describe("Action ID to execute (from entity_actions)"),
    token: z.string().describe("Bearer token from entity_possess"),
  },
  async ({ entityId, actionId, token }) =>
    api("POST", `/api/entity/${encodeURIComponent(entityId)}/act`, { actionId }, { Authorization: `Bearer ${token}` })
);

// ===================== POSSESSION =====================

server.tool(
  "entity_possess",
  "Take external control of an embodied NPC. Returns a bearer token needed for entity_act. The entity will no longer act autonomously until released.",
  { entityId: z.string().describe("Entity ID to possess (must be embodied)") },
  async ({ entityId }) => api("POST", "/api/entity/possess", { entityId })
);

server.tool(
  "entity_release",
  "Release external control of a possessed entity, returning it to autonomous AI control.",
  { entityId: z.string().describe("Entity ID to release") },
  async ({ entityId }) => api("POST", "/api/entity/release", { entityId })
);

// --- Start ---

const transport = new StdioServerTransport();
await server.connect(transport);
console.error("Autonome MCP server connected");
