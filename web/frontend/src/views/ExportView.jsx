import { useMemo, useState } from "react";
import { api } from "../lib/api.js";
import { formatNumber } from "../lib/format.js";
import { useApi } from "../hooks/useApi.js";
import { Card, CardHeader, PageHeader } from "../components/ui/Card.jsx";
import { Select } from "../components/ui/Controls.jsx";
import { Banner } from "../components/ui/Feedback.jsx";
import Button from "../components/ui/Button.jsx";

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
const input =
  "rounded-lg border border-[var(--border)] bg-[var(--surface)] px-2.5 py-1.5 text-xs text-[var(--ink)] shadow-sm focus:border-[var(--accent-ring)] focus:outline-none focus:ring-2 focus:ring-[var(--accent-soft)]";

function Field({ label, children }) {
  return (
    <label className="flex flex-col gap-1.5">
      <span className="text-[0.7rem] font-medium text-[var(--muted)]">{label}</span>
      {children}
    </label>
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
  const playerOpts = useMemo(() => [{ id: "all", label: "All players" }, ...(players.data || []).map((p) => ({ id: p.playerName, label: p.playerName }))], [players.data]);
  const badRange = from && to && from > to;

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
      setStatus({ kind, state: "error", message: err.message === "Failed to fetch" ? "can't reach the server" : err.message });
    }
  };

  return (
    <div className="space-y-6">
      <PageHeader eyebrow="Data" title="Export" description="Download raw sessions and events as CSV for thesis analysis in a spreadsheet, R or Python." />

      <Card className="overflow-hidden">
        <CardHeader title="What to include" subtitle="Dates filter on when a session started; both days are included" />
        <div className="grid gap-4 px-5 py-5 sm:grid-cols-2 lg:grid-cols-4">
          <Field label="Player">
            <Select label="Player" value={player} onChange={setPlayer} options={playerOpts} />
          </Field>
          <Field label="Level">
            <Select label="Level" value={level} onChange={setLevel} options={[{ id: "all", label: "All levels" }, { id: "Easy", label: "Level 1" }, { id: "Medium", label: "Level 2" }]} />
          </Field>
          <Field label="From">
            <input type="date" className={input} value={from} max={to || undefined} onChange={(e) => setFrom(e.target.value)} />
          </Field>
          <Field label="To">
            <input type="date" className={input} value={to} min={from || undefined} onChange={(e) => setTo(e.target.value)} />
          </Field>
        </div>
        {badRange && <p className="px-5 pb-4 text-xs text-[var(--bad)]">The start date is after the end date.</p>}
      </Card>

      <div className="grid gap-6 lg:grid-cols-2">
        <Card className="flex flex-col">
          <CardHeader title="Sessions" subtitle="One row per fight: player, level, status, outcome, score, XP, damage, hits, full-power hits, duration, HP left, start and end" />
          <div className="mt-auto px-5 py-4">
            <Button variant="primary" size="md" icon="download" disabled={badRange || status?.state === "loading"} onClick={() => download("sessions")}>
              {status?.kind === "sessions" && status.state === "loading" ? "Preparing…" : "Download sessions.csv"}
            </Button>
          </div>
        </Card>
        <Card className="flex flex-col">
          <CardHeader title="Events" subtitle="One row per event, with the common payload fields as columns (t, damage, swingSpeed, tier, hand, HP, XP, attack…) plus the full payload as JSON" />
          <div className="mt-auto flex flex-wrap items-center gap-3 px-5 py-4">
            <Select label="Event type" value={type} onChange={setType} options={EVENT_TYPES} />
            <Button variant="primary" size="md" icon="download" disabled={badRange || status?.state === "loading"} onClick={() => download("events")}>
              {status?.kind === "events" && status.state === "loading" ? "Preparing…" : "Download events.csv"}
            </Button>
          </div>
        </Card>
      </div>

      {status?.state === "done" && (
        <Banner tone="info" title={`Downloaded ${status.name}`}>
          {formatNumber(status.rows)} row{status.rows === 1 ? "" : "s"}
          {status.rows === 0 ? " — nothing matched these filters." : "."}
        </Banner>
      )}
      {status?.state === "error" && <Banner title="Export failed">The download didn't complete: {status.message}.</Banner>}
    </div>
  );
}
