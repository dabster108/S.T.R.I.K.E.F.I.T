# STRIKEFIT web platform

- `backend/`: Node + Express API on Render, data in Neon Postgres, live updates over Server-Sent Events. See `backend/README.md`.
- `frontend/`: Vite + React + Tailwind + React Router app on Vercel: the public landing page (`/`) and the real-time dashboard (`/dashboard/*`), in the original blue-and-white design. All colours, radii and shadows live in `frontend/src/styles/tokens.css`.

The Unity game (`Assets/Scripts/Networking/SessionReporter.cs`) posts to the backend while a fight is being played.

## Run locally

```bash
# terminal 1: API (needs web/backend/.env with DATABASE_URL)
cd web/backend && npm install && npm start          # http://localhost:4000

# terminal 2: site + dashboard, proxying /api to the API
cd web/frontend && npm install && npm run dev       # http://localhost:5173

# optional: play a fake fight so the live views move (deleted afterwards unless --keep)
cd web/backend && npm run simulate -- --url http://localhost:4000 --level Medium --seconds 30
```

| Env var | Where | Default | Purpose |
|---|---|---|---|
| `DATABASE_URL` | backend | (required) | Neon connection string. Never commit it. |
| `ALLOWED_ORIGINS` | backend | none | Comma-separated browser origins allowed by CORS, e.g. `https://strikefit.vercel.app`. `https://*.vercel.app` style wildcards work. localhost is always allowed. |
| `PORT` | backend | `4000` | Render sets this itself. |
| `BACKEND_URL` | frontend dev | `http://localhost:4000` | Where the Vite dev proxy sends `/api`. |
| `VITE_API_URL` | frontend build | same origin | Absolute API URL when the site is hosted elsewhere (Vercel). |

## Deploy

### Vercel (frontend)

| Setting | Value |
|---|---|
| Root Directory | `web/frontend` |
| Framework Preset | Vite |
| Build Command | `npm run build` |
| Output Directory | `dist` |
| Install Command | `npm install` (default) |
| Environment Variable | `VITE_API_URL` = `https://s-t-r-i-k-e-f-i-t.onrender.com` (Production and Preview) |

`frontend/vercel.json` rewrites every path to `index.html`, so `/dashboard/...` and `/dashboard/player/...` work on refresh and when opened directly.

### Render (backend)

Unchanged settings (Root Directory `web/backend`, build `npm install`, start `npm run start`, `DATABASE_URL`), plus one new environment variable:

- `ALLOWED_ORIGINS` = your Vercel URL, e.g. `https://strikefit.vercel.app` (add preview URLs or `https://*.vercel.app` if you want previews to work too).

The server applies `schema.sql` on every boot (all statements are `IF NOT EXISTS`), so the new columns are added automatically on the first deploy.

## Connect Unity

`SessionReporter` on the `BattleSystems` object (both level scenes) has:

- **Base Url**: `https://s-t-r-i-k-e-f-i-t.onrender.com` (https is required on Quest). For a backend on this Mac, use `http://localhost:4000` in the Editor only.
- **Player Name**: `Player 1` by default. The dashboard groups history by this exact name.
- **Send Live Events**: on. Off sends only the final result.
