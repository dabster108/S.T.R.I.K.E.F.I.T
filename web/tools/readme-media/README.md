# readme-media

Local-only tools that produce the images in `docs/images/` used by the main README. Not shipped, not deployed, not imported by the app, and no entry in any `package.json`.

| Script | Output | Needs |
|---|---|---|
| `hero-frames.mjs` + `build-gif.py` | `hero.gif` (1280x720, about 10 s, 12.5 fps, 256-colour palette, loops) and `hero-poster.png` | a running site, Brave, Python 3 + Pillow |
| `dashboard-shots.mjs` | `dashboard-overview.png`, `dashboard-live.png`, `dashboard-player.png`, `dashboard-scoreboard.png` (1440 px wide viewport) | a running site and a **local** backend, Brave |
| `monsters-composite.py` | `monsters.jpg` (1600 px wide, three portraits, labelled) | Python 3 + Pillow |

`cdp.mjs` is a small DevTools-protocol driver for headless Brave (`BROWSER_BIN` overrides the path). It loads `ws` from `web/backend/node_modules`, so run `npm install` in `web/backend` first. No ffmpeg or gifski is used.

## Safety

- The Live Arena shot posts a simulated fight and the demo data comes from `web/backend/scripts/simulate-live.js`, both of which **write** sessions. `dashboard-shots.mjs` refuses any `--api` that is not `localhost` or `127.0.0.1`.
- Point the local backend at a throwaway Postgres, never at the Neon database. One way without Docker: `npm i embedded-postgres` in a scratch folder outside the repo, start it on a spare port, set `DATABASE_URL=postgres://user:pass@127.0.0.1:PORT/db`, run `npm run db:schema` and `npm start` in `web/backend` on a spare `PORT`, then seed it with `npm run simulate -- --keep` using player names `Player 1`, `Player 2`, `Player 3` and `Guest`.
- Only those placeholder names should appear in a README image. Never capture a page that shows a token, a keyed URL or a real name.

## Regenerate

```bash
# throwaway backend on :4010 and the site on :5180 pointing at it
cd web/frontend && BACKEND_URL=http://localhost:4010 npx vite --port 5180 --strictPort &

cd web/tools/readme-media
node hero-frames.mjs --url http://localhost:5180 --out /tmp/hero-frames --fps 12.5
python3 build-gif.py --frames /tmp/hero-frames --out ../../../docs/images/hero.gif --poster ../../../docs/images/hero-poster.png --fps 12.5
node dashboard-shots.mjs --site http://localhost:5180 --api http://localhost:4010
python3 monsters-composite.py
```

`hero.gif` is about 4 MB. If it grows, lower `--colors` in `build-gif.py` or shorten the holds in `hero-frames.mjs`. The images are kept out of Git LFS by `docs/images/.gitattributes`.

## Not produced here

Game screenshots need the Unity editor in Play mode (Game view at 1920x1080, every `SessionReporter` base URL pointed at a dead local port so nothing reaches the production backend). Take them by hand or with Quest capture or scrcpy.
