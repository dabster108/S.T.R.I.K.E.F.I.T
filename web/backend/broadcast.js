// WebRTC signaling relay for the headset livestream: WS /api/broadcast/ws.
//
// The relay only forwards SDP and ICE between peers; video never passes through
// this server (it flows headset -> browser over WebRTC).
//
// Roles (query string ?role=streamer|viewer, optional &room=<name>, default "strikefit"):
//   streamer  the Unity game. Connects on launch and idles until a viewer asks for video,
//             so the headset needs no menu interaction. One per room; a new one replaces the old.
//   viewer    a browser on /broadcast. Many per room.
// If BROADCAST_KEY is set, a streamer must also pass &key=<BROADCAST_KEY>.
//
// Messages are JSON { type, ... }. The relay stamps `from` on everything a viewer
// sends and routes everything the streamer sends by its `to` (a viewer id):
//   relay -> any       welcome { role, id, room, streamerOnline }
//   relay -> viewer    streamer-status { online }
//   viewer -> streamer start | answer { sdp } | candidate { candidate, sdpMid, sdpMLineIndex } | stop
//   relay -> streamer  viewer-left { from }
//   streamer -> viewer offer { to, sdp } | candidate { to, ... } | error { to, message }

const { WebSocketServer } = require("ws");

const PATH = "/api/broadcast/ws";
const PING_MS = 25_000; // under Render's idle-connection timeout
const MAX_MESSAGE_BYTES = 64 * 1024; // an SDP is a few KB
const DEFAULT_ROOM = "strikefit";

const rooms = new Map(); // room -> { streamer: ws | null, viewers: Map<id, ws> }
let nextViewerId = 0;

function roomOf(name) {
  let room = rooms.get(name);
  if (!room) {
    room = { streamer: null, viewers: new Map() };
    rooms.set(name, room);
  }
  return room;
}

function dropRoomIfEmpty(name) {
  const room = rooms.get(name);
  if (room && !room.streamer && !room.viewers.size) rooms.delete(name);
}

function send(ws, msg) {
  if (ws && ws.readyState === ws.OPEN) ws.send(JSON.stringify(msg));
}

function toViewers(room, msg) {
  for (const v of room.viewers.values()) send(v, msg);
}

function parse(data) {
  try {
    const msg = JSON.parse(data.toString());
    return msg && typeof msg.type === "string" ? msg : null;
  } catch {
    return null;
  }
}

function attachStreamer(ws, roomName) {
  const room = roomOf(roomName);
  if (room.streamer) room.streamer.close(4000, "Replaced by a newer streamer");
  room.streamer = ws;
  send(ws, { type: "welcome", role: "streamer", id: "streamer", room: roomName, streamerOnline: true });
  toViewers(room, { type: "streamer-status", online: true });
  console.log(`[broadcast] streamer online in "${roomName}" (${room.viewers.size} viewer(s) waiting)`);

  ws.on("message", (data) => {
    const msg = parse(data);
    if (!msg || typeof msg.to !== "string") return;
    const { to, ...rest } = msg;
    send(room.viewers.get(to), rest);
  });

  ws.on("close", () => {
    if (room.streamer !== ws) return; // already replaced
    room.streamer = null;
    toViewers(room, { type: "streamer-status", online: false });
    console.log(`[broadcast] streamer offline in "${roomName}"`);
    dropRoomIfEmpty(roomName);
  });
}

function attachViewer(ws, roomName) {
  const room = roomOf(roomName);
  const id = `v${++nextViewerId}`;
  room.viewers.set(id, ws);
  send(ws, { type: "welcome", role: "viewer", id, room: roomName, streamerOnline: !!room.streamer });

  ws.on("message", (data) => {
    const msg = parse(data);
    if (!msg) return;
    send(room.streamer, { ...msg, from: id });
  });

  ws.on("close", () => {
    room.viewers.delete(id);
    send(room.streamer, { type: "viewer-left", from: id });
    dropRoomIfEmpty(roomName);
  });
}

// Shares the HTTP server with Express and Socket.IO. Socket.IO's engine only
// claims /socket.io/ upgrades, so this one claims PATH and leaves the rest alone.
function attach(server) {
  const wss = new WebSocketServer({ noServer: true, maxPayload: MAX_MESSAGE_BYTES });

  server.on("upgrade", (req, socket, head) => {
    const url = new URL(req.url, "http://localhost");
    if (url.pathname !== PATH) return;

    const role = url.searchParams.get("role");
    const roomName = (url.searchParams.get("room") || DEFAULT_ROOM).trim().slice(0, 64) || DEFAULT_ROOM;
    const key = process.env.BROADCAST_KEY;
    const rejected =
      role !== "streamer" && role !== "viewer"
        ? "400 Bad Request"
        : role === "streamer" && key && url.searchParams.get("key") !== key
          ? "401 Unauthorized"
          : null;
    if (rejected) {
      socket.end(`HTTP/1.1 ${rejected}\r\nConnection: close\r\n\r\n`);
      return;
    }

    wss.handleUpgrade(req, socket, head, (ws) => {
      ws.isAlive = true;
      ws.on("pong", () => (ws.isAlive = true));
      ws.on("error", () => ws.terminate());
      if (role === "streamer") attachStreamer(ws, roomName);
      else attachViewer(ws, roomName);
    });
  });

  const ping = setInterval(() => {
    for (const ws of wss.clients) {
      if (!ws.isAlive) {
        ws.terminate();
        continue;
      }
      ws.isAlive = false;
      ws.ping();
    }
  }, PING_MS);
  ping.unref();

  return wss;
}

function status() {
  const out = {};
  for (const [name, room] of rooms) out[name] = { streamerOnline: !!room.streamer, viewers: room.viewers.size };
  return out;
}

module.exports = { attach, status, PATH };
