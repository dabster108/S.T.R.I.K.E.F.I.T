# VR Fitness — Web Stats Stack

Node backend (`backend/`) that Unity posts battle results to, plus a React dashboard (`frontend/`) that shows them live.

## Run

Terminal 1 — backend (stores data in `backend/db.json`):

```bash
cd web/backend
npm install
npm start
```

Terminal 2 — dashboard in dev mode:

```bash
cd web/frontend
npm install
npm run dev
```

Open http://localhost:5173

**Single-port option:** run `npm run build` in `web/frontend` once. The backend then also serves the dashboard at http://localhost:4000, and at `http://<mac-ip>:4000` from any device on the same Wi-Fi.

| Env var | Where | Default | Purpose |
|---|---|---|---|
| `PORT` | backend | `4000` | Port to listen on |
| `DB_PATH` | backend | `backend/db.json` | Data file (use a copy for testing) |
| `BACKEND_URL` | frontend dev | `http://localhost:4000` | Where the Vite dev proxy sends `/api` + `/socket.io` |
| `VITE_API_URL` | frontend build | same origin | Absolute backend URL if the dashboard is hosted elsewhere |

## Connect Unity

Set **SessionReporter → Backend Base Url** (no trailing slash):

- Unity Editor on this Mac: `http://localhost:4000`
- Quest over Wi-Fi: `http://<mac-ip>:4000`. Find the IP with `ipconfig getifaddr en0`, or open the dashboard's **Connection** page, which lists it with a copy button.

Use the **Run connection test** button on the Connection page to check that HTTP and realtime both reach the backend.

## API

| Method | Path | Notes |
|---|---|---|
| GET | `/api/health` | Status, uptime, counts, connected clients, LAN URLs |
| GET | `/api/stats` | Totals, win rate, streaks, per-level breakdown, top-20 leaderboard |
| GET | `/api/sessions` | Newest first. Filters: `player`, `level`, `outcome`, `limit` |
| GET | `/api/sessions/:id` | One session plus its events |
| GET | `/api/players` | Per-player summaries |
| GET | `/api/events` | Newest first. Filters: `type`, `sessionId`, `limit` |
| POST | `/api/sessions` | Finished battle. Re-posting the same `id` updates it instead of duplicating it |
| POST | `/api/events` | `session_start`, `hit`, … (`ping` is broadcast but never stored) |
| DELETE | `/api/sessions/:id` | Remove one session and its events |
| DELETE | `/api/sessions` | Clear everything |

Realtime (Socket.io) broadcasts `session`, `event`, `stats`, `session_deleted` and `cleared`. The dashboard refetches everything on every (re)connect, so a backend restart never leaves it stale.

## What gets tracked

Each finished session: playerName, level, outcome (victory / defeat), score, damageDealt, hitCount, tapCount, durationSec, player/monster HP remaining, startedAt / endedAt.

## Frontend layout

```
frontend/src/
├── App.jsx                 app shell: routing, drawer, toasts
├── hooks/                  useTelemetry (REST + socket), useHashRoute
├── lib/                    api client, formatters, derived stats
├── components/
│   ├── ui/                 Card, Button, Badge, StatTile, Controls, Feedback, Toasts, Icon
│   ├── charts/             ScoreTrendChart, LevelBars, OutcomeSplit, Sparkline
│   ├── layout/             Sidebar + mobile nav
│   └── sessions/           SessionsTable, SessionDrawer, EventList, LiveSessionCard
└── views/                  Dashboard, Live, Sessions, Leaderboard, Players, Settings (Connection)
```
