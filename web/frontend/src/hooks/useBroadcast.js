import { useEffect, useRef, useState } from "react";
import { API_BASE } from "../lib/api.js";

// Viewer side of the headset livestream. Opens the signaling socket on mount
// (WS /api/broadcast/ws, see web/backend/broadcast.js), asks the headset for video
// as soon as it is online, and answers its WebRTC offer. Video flows headset ->
// browser directly; the backend only relays SDP and ICE.
//
// phase: "disconnected" | "connecting" | "negotiating" | "live"

const ICE_SERVERS = [{ urls: ["stun:stun.l.google.com:19302", "stun:stun1.l.google.com:19302", "stun:stun2.l.google.com:19302"] }];
const RECONNECT_MIN_MS = 1000;
const RECONNECT_MAX_MS = 15000;
const NEGOTIATION_TIMEOUT_MS = 20000; // no LIVE by then -> ask the headset again
const STATS_MS = 1000;

export function signalingUrl(room) {
  const base = API_BASE || window.location.origin;
  return `${base.replace(/^http/, "ws")}/api/broadcast/ws?role=viewer&room=${encodeURIComponent(room)}`;
}

const INITIAL = { phase: "connecting", headsetOnline: null, error: null, stream: null, stats: null };

export function useBroadcast(room = "strikefit") {
  const [state, setState] = useState(INITIAL);
  const reconnect = useRef(() => {});

  useEffect(() => {
    let disposed = false;
    let ws = null;
    let pc = null;
    let pendingCandidates = [];
    let retryTimer = null;
    let negotiationTimer = null;
    let statsTimer = null;
    let backoff = RECONNECT_MIN_MS;
    let lastBytes = null;

    const patch = (p) => !disposed && setState((s) => ({ ...s, ...p }));
    const send = (msg) => ws?.readyState === WebSocket.OPEN && ws.send(JSON.stringify(msg));

    function closePeer() {
      clearTimeout(negotiationTimer);
      clearInterval(statsTimer);
      pendingCandidates = [];
      lastBytes = null;
      if (pc) {
        pc.ontrack = pc.onicecandidate = pc.onconnectionstatechange = null;
        pc.close();
        pc = null;
      }
      patch({ stream: null, stats: null });
    }

    function requestStream() {
      closePeer();
      patch({ phase: "negotiating", error: null });
      send({ type: "start" });
      negotiationTimer = setTimeout(() => {
        if (pc?.connectionState !== "connected") requestStream();
      }, NEGOTIATION_TIMEOUT_MS);
    }

    async function pollStats() {
      if (!pc) return;
      const report = await pc.getStats();
      let video = null;
      let pair = null;
      report.forEach((r) => {
        if (r.type === "inbound-rtp" && r.kind === "video") video = r;
        if (r.type === "candidate-pair" && r.nominated && r.state === "succeeded") pair = r;
      });
      if (!video) return;
      const now = video.timestamp;
      const kbps = lastBytes ? Math.round(((video.bytesReceived - lastBytes.bytes) * 8) / (now - lastBytes.at)) : null;
      lastBytes = { bytes: video.bytesReceived, at: now };
      patch({
        stats: {
          width: video.frameWidth ?? null,
          height: video.frameHeight ?? null,
          fps: video.framesPerSecond != null ? Math.round(video.framesPerSecond) : null,
          kbps,
          rttMs: pair?.currentRoundTripTime != null ? Math.round(pair.currentRoundTripTime * 1000) : null,
          packetsLost: video.packetsLost ?? 0,
        },
      });
    }

    function createPeer() {
      const peer = new RTCPeerConnection({ iceServers: ICE_SERVERS });
      peer.onicecandidate = ({ candidate }) => {
        if (candidate) send({ type: "candidate", candidate: candidate.candidate, sdpMid: candidate.sdpMid, sdpMLineIndex: candidate.sdpMLineIndex });
      };
      peer.ontrack = (e) => patch({ stream: e.streams[0] || new MediaStream([e.track]) });
      peer.onconnectionstatechange = () => {
        if (peer !== pc) return;
        const s = peer.connectionState;
        if (s === "connected") {
          clearTimeout(negotiationTimer);
          patch({ phase: "live", error: null });
          clearInterval(statsTimer);
          statsTimer = setInterval(() => pollStats().catch(() => {}), STATS_MS);
        } else if (s === "disconnected") {
          patch({ phase: "negotiating" }); // ICE often recovers on its own
        } else if (s === "failed") {
          patch({ error: "The video connection failed (blocked UDP or no route between the networks). Retrying…" });
          requestStream();
        }
      };
      return peer;
    }

    async function onOffer(sdp) {
      closePeer();
      pc = createPeer();
      const peer = pc;
      await peer.setRemoteDescription({ type: "offer", sdp });
      for (const c of pendingCandidates) await peer.addIceCandidate(c);
      pendingCandidates = [];
      const answer = await peer.createAnswer();
      await peer.setLocalDescription(answer);
      if (peer === pc) send({ type: "answer", sdp: answer.sdp });
      negotiationTimer = setTimeout(() => {
        if (pc === peer && peer.connectionState !== "connected") requestStream();
      }, NEGOTIATION_TIMEOUT_MS);
    }

    async function onCandidate(msg) {
      const c = { candidate: msg.candidate, sdpMid: msg.sdpMid, sdpMLineIndex: msg.sdpMLineIndex };
      if (pc?.remoteDescription) await pc.addIceCandidate(c);
      else pendingCandidates.push(c);
    }

    function onMessage(e) {
      let msg;
      try {
        msg = JSON.parse(e.data);
      } catch {
        return;
      }
      switch (msg.type) {
        case "welcome":
          backoff = RECONNECT_MIN_MS;
          patch({ headsetOnline: msg.streamerOnline });
          if (msg.streamerOnline) requestStream();
          else patch({ phase: "connecting" });
          break;
        case "streamer-status":
          patch({ headsetOnline: msg.online });
          if (msg.online) requestStream();
          else {
            closePeer();
            patch({ phase: "connecting" });
          }
          break;
        case "offer":
          onOffer(msg.sdp).catch((err) => {
            patch({ error: `Could not accept the headset's offer: ${err.message}` });
            requestStream();
          });
          break;
        case "candidate":
          onCandidate(msg).catch(() => {}); // a stale candidate after a restart is harmless
          break;
        case "error":
          patch({ error: msg.message || "The headset reported an error." });
          break;
        default:
      }
    }

    function connect() {
      if (disposed) return;
      patch({ phase: "connecting" });
      ws = new WebSocket(signalingUrl(room));
      ws.onmessage = onMessage;
      ws.onclose = () => {
        closePeer();
        if (disposed) return;
        patch({ phase: "disconnected", headsetOnline: null });
        retryTimer = setTimeout(connect, backoff);
        backoff = Math.min(backoff * 2, RECONNECT_MAX_MS);
      };
    }

    reconnect.current = () => {
      clearTimeout(retryTimer);
      backoff = RECONNECT_MIN_MS;
      if (ws && ws.readyState === WebSocket.OPEN) requestStream();
      else {
        if (ws) ws.onclose = null;
        ws?.close();
        connect();
      }
    };

    connect();

    return () => {
      disposed = true;
      clearTimeout(retryTimer);
      send({ type: "stop" });
      closePeer();
      if (ws) {
        ws.onclose = ws.onmessage = null;
        ws.close();
      }
    };
  }, [room]);

  return { ...state, retry: () => reconnect.current() };
}
