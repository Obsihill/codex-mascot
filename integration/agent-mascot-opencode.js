// MascotBridge: read-only OpenCode event relay. Never persist prompts or tool arguments.
import { mkdir, writeFile, rename } from "node:fs/promises"
import { join } from "node:path"
import { randomUUID } from "node:crypto"

const eventDirectory = process.env.AGENT_MASCOT_EVENTS_OPENCODE || __AGENT_MASCOT_EVENTS__

export const AgentMascotPlugin = async ({ directory }) => {
  const paths = new Map()
  const childSessions = new Set()
  return {
    event: async ({ event }) => {
      try {
        const type = event?.type
        const properties = event?.properties ?? {}
        if (type === "session.updated" || type === "session.created") {
          const info = properties.info
          if (info?.id) {
            paths.set(info.id, info.directory || directory)
            if (info.parentID) childSessions.add(info.id)
          }
          return
        }
        const name = type === "session.status" && properties.status?.type === "busy" ? "session.busy" : type
        if (!["session.busy", "session.idle", "session.error", "session.deleted",
              "permission.asked", "permission.replied", "question.asked", "question.replied"].includes(name)) return
        const id = properties.sessionID || properties.info?.id
        if (typeof id !== "string" || !id || childSessions.has(id)) return
        const record = {
          version: 1,
          timestamp: new Date().toISOString(),
          session_id: id,
          cwd: paths.get(id) || directory,
          hook_event_name: name,
          request_id: properties.requestID || properties.id || ""
        }
        await mkdir(eventDirectory, { recursive: true })
        const file = `${Date.now()}-${process.hrtime.bigint().toString().padStart(20, "0")}-${randomUUID()}`
        const temp = join(eventDirectory, file + ".tmp")
        await writeFile(temp, JSON.stringify(record), "utf8")
        await rename(temp, join(eventDirectory, file + ".json"))
        if (name === "session.deleted") paths.delete(id)
      } catch {
        // Monitoring must never interrupt an OpenCode session.
      }
    }
  }
}
