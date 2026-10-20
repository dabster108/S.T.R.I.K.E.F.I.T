import { useState } from "react";
import { API_BASE, api } from "../lib/api.js";
import { formatDuration, formatRelative } from "../lib/format.js";
import { Card, CardHeader, PageHeader } from "../components/ui/Card.jsx";
import { ConfirmDialog } from "../components/ui/Feedback.jsx";
import Button from "../components/ui/Button.jsx";
import Icon from "../components/ui/Icon.jsx";

const ENDPOINTS = [
  ["GET", "/api/health", "Status, counts, LAN addresses"],
  ["GET", "/api/stats", "Aggregates, per-level breakdown, leaderboard"],
  ["GET", "/api/sessions", "All sessions (filters: player, level, outcome, limit)"],
  ["GET", "/api/sessions/:id", "One session + its events"],
  ["GET", "/api/players", "Per-player summaries"],
  ["GET", "/api/events", "Recent events (filters: type, sessionId, player, limit)"],
  ["GET", "/api/stream", "Live updates (Server-Sent Events, ?player=)"],
  ["GET", "/api/live", "Fights in progress with HP / XP / score"],
  ["GET", "/api/players/:name", "One player's profile"],
  ["GET", "/api/overview", "Dashboard totals, sessions per day, per level"],
  ["GET", "/api/public-stats", "Aggregate numbers only (landing page)"],
  ["GET", "/api/analytics", "Swing speed, effort and level comparisons"],
  ["GET", "/api/export/:kind.csv", "sessions.csv or events.csv"],
  ["POST", "/api/sessions", "Unity: live session start, final result (same id = update)"],
  ["POST", "/api/events", "Unity: combat events, single or { events: [...] }"],
  ["DELETE", "/api/sessions/:id", "Remove one session"],
  ["DELETE", "/api/sessions", "Clear everything"],
];

const METHOD_CLS = {
  GET: "bg-blue-50 text-blue-700",
  POST: "bg-emerald-50 text-emerald-700",
  DELETE: "bg-red-50 text-red-700",
};

function CopyField({ value }) {
  const [copied, setCopied] = useState(false);
  return (
    <div className="flex items-center gap-2 rounded-lg border border-[var(--border)] bg-[var(--surface-muted)] py-1.5 pl-3 pr-1.5">
      <code className="flex-1 truncate border-0 bg-transparent p-0 text-xs text-[var(--ink)]">{value}</code>
      <Button
        icon={copied ? "check" : "copy"}
        onClick={() => {
          navigator.clipboard?.writeText(value).then(() => {
            setCopied(true);
            setTimeout(() => setCopied(false), 1500);
          });
        }}
      >
        {copied ? "Copied" : "Copy"}
      </Button>
    </div>
  );
}

function Step({ n, children }) {
  return (
    <li className="flex gap-3">
      <span className="flex h-6 w-6 shrink-0 items-center justify-center rounded-full bg-[var(--accent-soft)] text-xs font-bold text-[var(--accent)]">{n}</span>
      <div className="min-w-0 flex-1 space-y-2">{children}</div>
    </li>
  );
}

function Row({ label, children }) {
  return (
    <div className="flex items-center justify-between gap-4 px-5 py-3 text-sm">
      <span className="font-semibold text-[var(--muted)]">{label}</span>
      <span className="truncate text-right font-bold tabular-nums text-[var(--ink)]">{children}</span>
    </div>
  );
}

export default function SettingsView({ connection, health, checkHealth, pingRoundTrip, refresh, toast }) {
  const [test, setTest] = useState(null);
  const [testing, setTesting] = useState(false);
  const [confirm, setConfirm] = useState(false);
  const [clearing, setClearing] = useState(false);

  const origin = API_BASE || window.location.origin;
  // Only meaningful when the backend runs on this machine (on Render it's a private address).
  const isLocal = /^(localhost|127\.0\.0\.1|\[::1\])$/.test(new URL(origin).hostname);
  const lanUrl = health?.lanUrls?.[0] || `http://localhost:${health?.port || 4000}`;

  async function runTest() {
    setTesting(true);
    setTest(null);
    try {
      await checkHealth();
      setTest({ ok: true, ...(await pingRoundTrip()) });
    } catch (e) {
      setTest({ ok: false, error: e.message });
    } finally {
      setTesting(false);
    }
  }

  async function clearAll() {
    setClearing(true);
    try {
      await api.clearAll();
      toast({ tone: "info", title: "Database cleared", detail: "All sessions and events were removed." });
      setConfirm(false);
    } catch (e) {
      toast({ tone: "bad", title: "Clear failed", detail: e.message });
    } finally {
      setClearing(false);
    }
  }

  return (
    <div className="space-y-6">
      <PageHeader
        eyebrow="Connection"
        title="Backend & Unity setup"
        description="Check the backend is reachable, test the live channel, and see how the headset reports fights."
      />

      <div className="grid gap-6 xl:grid-cols-2">
        <Card className="overflow-hidden">
          <CardHeader
            title="Backend status"
            subtitle={health?.checkedAt ? `Checked ${formatRelative(health.checkedAt)}` : "Not checked yet"}
            actions={
              <Button icon="refresh" onClick={() => (checkHealth(), refresh())}>
                Refresh
              </Button>
            }
          />
          <div className="divide-y divide-[var(--border)]">
            <Row label="API address">{origin}</Row>
            <Row label="Health">
              {health?.ok ? (
                <span className="text-[var(--good-ink)]">OK · {health.latencyMs} ms</span>
              ) : (
                <span className="text-[var(--bad)]">{health?.error || "Unreachable"}</span>
              )}
            </Row>
            <Row label="Realtime stream">
              <span>{{ live: "Connected", connecting: "Reconnecting", offline: "Offline" }[connection] || connection}</span>
            </Row>
            <Row label="Uptime">{health?.ok ? formatDuration(health.uptimeSec) : "—"}</Row>
            <Row label="Stored sessions / events">{health?.ok ? `${health.sessions} / ${health.events}` : "—"}</Row>
            <Row label="Open dashboards">{health?.ok ? (health.streamClients ?? health.clients) : "—"}</Row>
          </div>
          <div className="border-t border-[var(--border)] bg-[var(--surface-muted)] px-5 py-4">
            <div className="flex flex-wrap items-center gap-3">
              <Button variant="primary" size="md" icon="signal" onClick={runTest} disabled={testing}>
                {testing ? "Testing…" : "Run connection test"}
              </Button>
              {test?.ok && (
                <span className="flash-in text-sm text-[var(--good-ink)]">
                  <Icon name="check" className="mr-1 inline h-4 w-4" />
                  HTTP {test.httpMs} ms · realtime echo {test.socketMs} ms
                </span>
              )}
              {test && !test.ok && (
                <span className="flash-in text-sm text-[var(--bad)]">
                  <Icon name="xCircle" className="mr-1 inline h-4 w-4" />
                  {test.error}
                </span>
              )}
            </div>
            <p className="mt-2 text-xs text-[var(--muted)]">
              Sends a ping through <code>POST /api/events</code> and waits for it to come back over the live stream. Pings aren't saved.
            </p>
          </div>
        </Card>

        <Card className="overflow-hidden">
          <CardHeader title="Connect the headset" subtitle="How the game reports fights to this dashboard" />
          <div className="space-y-5 px-5 py-5">
            <ol className="space-y-4 text-sm text-[var(--ink-secondary)]">
              <Step n={1}>
                <p>
                  The game sends every fight to this backend. It&apos;s already set in <strong className="font-semibold text-[var(--ink)]">SessionReporter → Base Url</strong>; the
                  Quest only accepts https addresses.
                </p>
                <CopyField value={origin} />
              </Step>
              <Step n={2}>
                <p>
                  Players pick their name in the headset before each fight: point at a name and pull the trigger. If nobody picks within 10 seconds, the last name
                  used (or Player 1) is taken. Edit the list in <strong className="font-semibold text-[var(--ink)]">BattleSystems → PlayerNamePicker</strong>.
                </p>
              </Step>
              <Step n={3}>
                <p>
                  Press Start and the fight appears in the Live Arena. If the Wi-Fi drops, the game keeps playing; that fight just isn&apos;t recorded.
                </p>
              </Step>
            </ol>
            {isLocal && (
              <div className="space-y-2 border-t border-[var(--border)] pt-4 text-xs text-[var(--muted)]">
                <p>
                  Backend running on this Mac: Editor Play mode can use <code>http://localhost:4000</code>. A headset on the same Wi-Fi can reach it at:
                </p>
                <CopyField value={lanUrl} />
              </div>
            )}
          </div>
        </Card>
      </div>

      <Card className="overflow-hidden">
        <CardHeader title="API reference" subtitle="Everything the backend exposes" />
        <ul className="divide-y divide-[var(--border)]">
          {ENDPOINTS.map(([method, path, desc]) => (
            <li key={`${method} ${path}`} className="flex flex-wrap items-center gap-x-3 gap-y-1 px-5 py-2.5 text-sm">
              <span className={`w-16 rounded-md px-1.5 py-0.5 text-center font-mono text-xs font-bold ${METHOD_CLS[method]}`}>{method}</span>
              <code className="border-0 bg-transparent p-0 text-[var(--ink)]">{path}</code>
              <span className="text-xs text-[var(--muted)] sm:ml-auto">{desc}</span>
            </li>
          ))}
        </ul>
      </Card>

      {/* Deleting every session is a one-click wipe on a site with no login: offered on a local dev build only (npm run dev:safe). */}
      {import.meta.env.DEV && (
      <Card className="border-red-200">
        <div className="flex flex-wrap items-center justify-between gap-4 px-5 py-4">
          <div>
            <h2 className="text-sm font-bold text-[var(--ink)]">Clear all data</h2>
            <p className="mt-0.5 text-xs text-[var(--muted)]">Deletes every session and event from the database. Can&apos;t be undone.</p>
          </div>
          <Button variant="danger" icon="trash" onClick={() => setConfirm(true)} disabled={!health?.ok}>
            Clear database
          </Button>
        </div>
      </Card>
      )}

      <ConfirmDialog
        open={confirm}
        title="Clear the whole database?"
        description="Every recorded session and event will be permanently removed from the database."
        confirmLabel="Clear everything"
        busy={clearing}
        onConfirm={clearAll}
        onCancel={() => setConfirm(false)}
      />
    </div>
  );
}
