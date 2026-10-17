import { useMemo, useState } from "react";
import { api } from "../lib/api.js";
import { formatNumber } from "../lib/format.js";
import { useApi } from "../hooks/useApi.js";
import { useLiveMessages } from "../live/LiveProvider.jsx";
import { Card, PageHeader } from "../components/ui/Card.jsx";
import { inputClass } from "../components/ui/Controls.jsx";
import { Select } from "../components/ui/Select.jsx";
import Button from "../components/ui/Button.jsx";
import Icon from "../components/ui/Icon.jsx";

const EVENT_TYPES = [
  { id: "all", label: "All event types" },
  { id: "hit", label: "Hits (speed, tier, damage)" },
  { id: "hp", label: "HP snapshots (1 per second)" },
  { id: "weak_swing", label: "Swings too slow to count" },
  { id: "monster_attack", label: "Monster attacks" },
  { id: "player_hurt", label: "Damage taken" },
  { id: "dodge", label: "Dodges" },
  { id: "clash", label: "Weapon clashes" },
  { id: "session_complete", label: "Session results" },
];
const LEVEL_OPTS = [
  { id: "all", label: "All levels" },
  { id: "Easy", label: "Level 1" },
  { id: "Medium", label: "Level 2" },
];

// The exact CSV headers the backend writes (web/backend/analytics.js).
const SESSION_COLS = ["id", "playerName", "level", "status", "outcome", "score", "xp", "damageDealt", "hitCount", "fullSwings", "tapCount", "durationSec", "playerHpRemaining", "monsterHpRemaining", "startedAt", "endedAt", "source", "notes"];
const EVENT_COLS = ["id", "at", "sessionId", "playerName", "level", "type", "message", "t", "damage", "swingSpeed", "tier", "hand", "defended", "score", "xp", "hits", "playerHp", "playerMaxHp", "monsterHp", "monsterMaxHp", "attack", "category", "amount", "blocked", "timeLeft", "payload"];

function Field({ label, children }) {
  return (
    <label className="flex flex-col gap-1.5">
      <span className="text-xs font-semibold text-[var(--ink-secondary)]">{label}</span>
      {children}
    </label>
  );
}

function Step({ n, title, subtitle, children, className = "" }) {
  return (
    <section className={className}>
      <div className="flex items-center gap-3">
        <span className="t-index">{String(n).padStart(2, "0")}</span>
        <h2 className="t-kicker text-[var(--ink-secondary)]">{title}</h2>
        <span className="h-px flex-1 bg-[var(--border)]" aria-hidden />
      </div>
      {subtitle && <p className="measure mt-2 text-xs font-medium text-[var(--muted)]">{subtitle}</p>}
      <div className="mt-4">{children}</div>
    </section>
  );
}

function Columns({ cols }) {
  const [open, setOpen] = useState(false);
  const shown = open ? cols : cols.slice(0, 8);
  return (
    <div>
      <ul className="flex flex-wrap gap-1.5" aria-label="Columns">
        {shown.map((c) => (
          <li key={c} className="rounded-md bg-[var(--surface-muted)] px-1.5 py-0.5 font-mono text-xs font-semibold text-[var(--ink-secondary)] ring-1 ring-inset ring-[var(--border)]">
            {c}
          </li>
        ))}
      </ul>
      {cols.length > 8 && (
        <button type="button" onClick={() => setOpen((o) => !o)} aria-expanded={open} className="press mt-2 rounded text-xs font-semibold text-[var(--accent)] hover:underline">
          {open ? "Show fewer columns" : `Show all ${cols.length} columns`}
        </button>
      )}
    </div>
  );
}

function Status({ status }) {
  if (!status) return null;
  if (status.state === "done")
    return (
      <p className="fade-in flex items-center gap-1.5 text-xs font-semibold text-[var(--good-ink)]" role="status">
        <Icon name="check" className="h-3.5 w-3.5" />
        Saved {status.name} · {formatNumber(status.rows)} row{status.rows === 1 ? "" : "s"}
        {status.rows === 0 && " (nothing matched these filters)"}
      </p>
    );
  if (status.state === "error")
    return (
      <p className="fade-in flex items-center gap-1.5 text-xs font-semibold text-[var(--bad)]" role="alert">
        <Icon name="info" className="h-3.5 w-3.5" />
        The download didn't finish: {status.message}. Try again in a moment.
      </p>
    );
  return null;
}

function Dataset({ title, file, description, cols, busy, disabled, onDownload, status, children }) {
  return (
    <Card className="lift flex flex-col p-5 sm:p-6">
      <div className="grid gap-4 sm:grid-cols-[minmax(0,1fr)_auto] sm:items-start">
        <div className="min-w-0">
          <p className="t-kicker">{file}</p>
          <h3 className="mt-1.5 text-lg font-extrabold tracking-tight text-[var(--ink)]">{title}</h3>
          <p className="measure mt-1 text-sm font-medium leading-relaxed text-[var(--muted)]">{description}</p>
        </div>
        <p className="text-left sm:text-right">
          <span className="t-figure-md block text-[var(--ink)]">{cols.length}</span>
          <span className="t-kicker">columns</span>
        </p>
      </div>
      <div className="mt-4">
        <Columns cols={cols} />
      </div>
      <div className="mt-auto space-y-3 pt-5">
        <div className="flex flex-wrap items-center gap-3">
          {children}
          <Button variant="primary" size="md" icon={busy ? "loader" : "download"} disabled={disabled} onClick={onDownload}>
            {busy ? "Preparing…" : `Download ${file}`}
          </Button>
        </div>
        <Status status={status} />
      </div>
    </Card>
  );
}

export default function ExportView() {
  const players = useApi(() => api.players(), []);
  const [player, setPlayer] = useState("all");
  const [level, setLevel] = useState("all");
  const [from, setFrom] = useState("");
  const [to, setTo] = useState("");
  const [type, setType] = useState("all");
  const [status, setStatus] = useState(null);
  useLiveMessages((type, payload) => {
    if (type !== "player_renamed") return;
    players.reload();
    if (player === payload.oldName) setPlayer(payload.newName);
  });
  const playerOpts = useMemo(() => [{ id: "all", label: "All players" }, ...(players.data || []).map((p) => ({ id: p.playerName, label: p.playerName }))], [players.data]);
  const badRange = from && to && from > to;
  const busy = status?.state === "loading";
  const active = [player !== "all" && player, level !== "all" && LEVEL_OPTS.find((l) => l.id === level)?.label, from && `from ${from}`, to && `to ${to}`].filter(Boolean);

  const download = async (kind) => {
    setStatus({ kind, state: "loading" });
    try {
      const res = await fetch(api.exportUrl(kind, { player, level, from, to, type: kind === "events" ? type : undefined }));
      if (!res.ok) throw new Error(`the server answered ${res.status}`);
      const blob = await res.blob();
      const rows = Number(res.headers.get("X-Row-Count"));
      const name = /filename="([^"]+)"/.exec(res.headers.get("Content-Disposition") || "")?.[1] || `strikefit-${kind}.csv`;
      const url = URL.createObjectURL(blob);
      const a = Object.assign(document.createElement("a"), { href: url, download: name });
      document.body.appendChild(a);
      a.click();
      a.remove();
      setTimeout(() => URL.revokeObjectURL(url), 5000);
      setStatus({ kind, state: "done", rows, name });
    } catch (err) {
      setStatus({ kind, state: "error", message: err.message === "Failed to fetch" ? "the server didn't answer" : err.message });
    }
  };
  const statusFor = (kind) => (status?.kind === kind ? status : null);

  return (
    <div className="space-y-8">
      <PageHeader eyebrow="Data" title="Export" description="Download raw sessions and events as CSV for thesis analysis in a spreadsheet, R or Python." />

      <div className="grid gap-10 lg:grid-cols-12 lg:gap-8">
        <Step n={1} title="Choose what to include" subtitle="Dates filter on when a session started; both days are included." className="lg:sticky lg:top-20 lg:col-span-4 lg:self-start">
          <Card className="p-5">
            <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-1">
              <Field label="Player">
                <Select label="Player" value={player} onChange={setPlayer} options={playerOpts} />
              </Field>
              <Field label="Level">
                <Select label="Level" value={level} onChange={setLevel} options={LEVEL_OPTS} />
              </Field>
              <Field label="From">
                <input type="date" className={inputClass} value={from} max={to || undefined} onChange={(e) => setFrom(e.target.value)} />
              </Field>
              <Field label="To">
                <input type="date" className={inputClass} value={to} min={from || undefined} onChange={(e) => setTo(e.target.value)} />
              </Field>
            </div>
            <div className="mt-4 flex flex-wrap items-center gap-2 border-t border-[var(--border)] pt-4 text-xs font-semibold text-[var(--ink-secondary)]">
              <Icon name="filter" className="h-3.5 w-3.5 text-[var(--muted)]" />
              {active.length ? (
                active.map((f) => (
                  <span key={f} className="rounded-full bg-[var(--accent-soft)] px-2 py-0.5 text-[var(--accent-strong)] ring-1 ring-inset ring-[var(--accent-ring)]">
                    {f}
                  </span>
                ))
              ) : (
                <span>Everything recorded, all players and levels</span>
              )}
              {badRange && <span className="font-bold text-[var(--bad)]">The start date is after the end date.</span>}
            </div>
          </Card>
        </Step>

        <Step n={2} title="Pick a dataset" subtitle="Both files use the filters on the left. Column names match the headers in the CSV." className="lg:col-span-8">
          <div className="space-y-5">
            <Dataset
              title="Sessions"
              file="sessions.csv"
              description="One row per fight: who played which level, how it ended, score, XP, damage, hits and timing."
              cols={SESSION_COLS}
              busy={busy && status.kind === "sessions"}
              disabled={badRange || busy}
              onDownload={() => download("sessions")}
              status={statusFor("sessions")}
            />
            <Dataset
              title="Events"
              file="events.csv"
              description="One row per event, with the common payload fields as columns plus the full payload as JSON."
              cols={EVENT_COLS}
              busy={busy && status.kind === "events"}
              disabled={badRange || busy}
              onDownload={() => download("events")}
              status={statusFor("events")}
            >
              <Select label="Event type" value={type} onChange={setType} options={EVENT_TYPES} />
            </Dataset>
          </div>
        </Step>
      </div>
    </div>
  );
}
