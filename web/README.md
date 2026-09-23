# VR Fitness — Web Stats Stack

## Backend URL (shared)

See [`BACKEND_URL.json`](./BACKEND_URL.json).

Default (same machine / Unity Editor):

```
http://localhost:4000
```

Unity posts sessions to:

```
http://localhost:4000/api/sessions
```

On a Quest headset over Wi‑Fi, use your Mac LAN IP instead of `localhost`, e.g.:

```
http://192.168.1.42:4000/api/sessions
```

Find IP: `ipconfig getifaddr en0`

Update **RoundManager → Backend Url** in the Unity Inspector to match.

---

## Run

Terminal 1 — backend (writes `backend/db.json`):

```bash
cd web/backend
npm install
npm start
```

Terminal 2 — React stats board:

```bash
cd web/frontend
npm install
npm run dev
```

Open: http://localhost:5173

---

## What gets tracked (`db.json`)

Each finished play session stores:

- playerName, level, outcome (victory / defeat)
- score / pointsEarned
- damageDealt, hitCount, tapCount
- durationSec, player/monster HP remaining
- startedAt / endedAt

Realtime: Socket.io pushes `session`, `event`, and `stats` to the React board while you play.
