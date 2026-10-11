// MascotBridge: local Amp plugin. Store lifecycle metadata only, never prompts or messages.
import { mkdir, writeFile, rename } from "node:fs/promises"
import { join } from "node:path"
import { randomUUID } from "node:crypto"

const eventDirectory = process.env.AGENT_MASCOT_EVENTS_AMP || __AGENT_MASCOT_EVENTS__

export default function AgentMascotPlugin(amp) {
  async function emit(name, event, ctx) {
    try {
      const sessionId = event?.thread?.id
      if (typeof sessionId !== "string" || !sessionId) return
      const root = ctx?.system?.workspaceRoot
      const record = {
        version: 1,
        timestamp: new Date().toISOString(),
        session_id: sessionId,
        turn_id: typeof event.id === "string" ? event.id : null,
        cwd: root ? amp.helpers.filePathFromURI(root) : "",
        hook_event_name: name,
        agent_status: name === "agent.end" ? event.status : null
      }
      await mkdir(eventDirectory, { recursive: true })
      const file = `${Date.now()}-${process.hrtime.bigint().toString().padStart(20, "0")}-${randomUUID()}`
      const temp = join(eventDirectory, file + ".tmp")
      await writeFile(temp, JSON.stringify(record), "utf8")
      await rename(temp, join(eventDirectory, file + ".json"))
    } catch {
      // Mascot failures must never interrupt an Amp turn.
    }
  }

  amp.on("agent.start", (event, ctx) => emit("agent.start", event, ctx))
  amp.on("agent.end", (event, ctx) => emit("agent.end", event, ctx))
}
