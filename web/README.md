# STRIKEFIT web platform

- `backend/`: Node + Express API on Render, data in Neon Postgres, live updates over Server-Sent Events. See `backend/README.md`.
- `frontend/`: Vite + React + Tailwind + React Router app on Vercel: the public landing page (`/`) and the real-time dashboard (`/dashboard/*`), in the original blue-and-white design. All colours, radii and shadows live in `frontend/src/styles/tokens.css`.

The Unity game (`Assets/Scripts/Networking/SessionReporter.cs`) posts to the backend while a fight is being played.

## Run locally (safe by default)

A process on your laptop **never uses the production database**. `web/backend/.env` still holds the production `DATABASE_URL` for Render's sake, but a development process ignores that line and refuses to start if its `DATABASE_URL` is anything other than this machine (or a host you list in `DEV_DB_ALLOWED_HOSTS`). `ALLOW_PROD_DB=1` overrides the refusal on purpose; nothing sets it for you. Render counts as production because it always sets `RENDER=true` (elsewhere set `NODE_ENV=production`).

```bash
# one-time: install both packages (the dev database, PGlite, comes with the backend's dev dependencies: no Docker, no Postgres install)
cd web/backend && npm install && cd ../frontend && npm install && cd ..

# everything, against a local PGlite database (Postgres in WebAssembly) on 127.0.0.1:54329, data in web/backend/.dev-db:
cd web && npm run dev:safe
#   site + dashboard  http://localhost:5173      API  http://localhost:4000
#   first run: creates the container, applies the schema, seeds 15 simulated fights (players ZZTEST-ann/bo/cy/dee)
#   if 4000 or 5173 are busy:  PORT=4100 WEB_PORT=5273 npm run dev:safe
#   skip the seed:             npm run dev:safe -- --no-seed

# play a fake fight so the Live Arena moves (names must start with ZZTEST; the fight is deleted afterwards unless --keep)
cd web/backend && npm run simulate -- --url http://localhost:4000 --player ZZTEST-me --level Hard --seconds 70
#   --level Easy|Medium|Hard   --outcome victory|defeat|abandoned   --fast (no waiting)   --keep

# the database on its own
npm run dev:db              # run it in this terminal (Ctrl+C stops it); npm run dev:db -- reset | status
npm run dev:seed            # seed an already running dev backend
npm run dev:seed-study      # 10 ZZTEST study participants (consent, pre/post answers, fights) for the Lab's study section
                            #   -- --reset removes them first; refuses anything but the local dev database; add --url for other ports
```

The playtest study lives at `/study` (public). Participants get a code `P01`..`P20` that they use as the headset player name; the Thesis Lab's
"Playtest study" section and `GET /api/study/export.csv` read the answers. The Borg RPE is stored in `session_feedback` like any fight rating.

| Env var | Where | Default | Purpose |
|---|---|---|---|
| `DATABASE_URL` | backend | (required) | Production: Neon connection string (Render dashboard). Development: set by `.env.development`, local only. Never commit it. |
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
