// Server-Sent Events hub: GET /api/stream.
//
// One Render instance, so an in-process Set of open responses is the whole
// fan-out. (Postgres LISTEN/NOTIFY is not an option: Neon's pooled endpoint
// runs PgBouncer in transaction mode, which drops session-level LISTEN.)
//
// Every message is a named SSE event whose data is { type, payload, at }.
// Types: hello, heartbeat, session_started, session_updated, session_finished,
// event_created, session_deleted, player_renamed ({ oldName, newName, sessions, events }),
// feedback_saved ({ sessionId, playerName, level, enjoyment, rpe }).

const HEARTBEAT_MS = 15_000;
const RETRY_MS = 3_000;

const clients = new Set();
let nextId = 0;

const sameName = (a, b) => String(a).trim().toLowerCase() === String(b).trim().toLowerCase();

function frame(type, payload, id) {
  const data = JSON.stringify({ type, payload, at: new Date().toISOString() });
  return `${id ? `id: ${id}\n` : ""}event: ${type}\ndata: ${data}\n\n`;
}

// Writes to one client; a socket that has gone away is dropped instead of throwing.
function send(client, text) {
  try {
    client.res.write(text);
  } catch {
    clients.delete(client);
  }
}

function open(req, res) {
  const player = typeof req.query.player === "string" && req.query.player.trim() ? req.query.player.trim() : null;

  res.status(200).set({
    "Content-Type": "text/event-stream; charset=utf-8",
    "Cache-Control": "no-cache, no-transform",
    Connection: "keep-alive",
    "X-Accel-Buffering": "no", // tell any nginx-style proxy not to buffer
  });
  res.flushHeaders();
  req.socket.setNoDelay(true);
  req.socket.setKeepAlive(true);

  const client = { res, player };
  clients.add(client);
  res.write(`retry: ${RETRY_MS}\n\n`);
  send(client, frame("hello", { player, clients: clients.size, heartbeatMs: HEARTBEAT_MS }));

  req.on("close", () => clients.delete(client));
}

// playerName scopes the message: clients opened with ?player=X only receive
// messages about X. Messages without a player (e.g. clear-all) go to everyone.
function broadcast(type, payload, playerName = null) {
  if (!clients.size) return;
  const text = frame(type, payload, ++nextId);
  for (const client of clients) {
    if (client.player && playerName && !sameName(client.player, playerName)) continue;
    send(client, text);
  }
}

const heartbeat = setInterval(() => {
  if (!clients.size) return;
  const text = frame("heartbeat", { clients: clients.size });
  for (const client of clients) send(client, text);
}, HEARTBEAT_MS);
heartbeat.unref();

module.exports = { open, broadcast, clientCount: () => clients.size, HEARTBEAT_MS };
