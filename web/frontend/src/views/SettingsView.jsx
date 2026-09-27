import { useState } from "react";
import { API_BASE, api } from "../lib/api.js";
import { formatDuration, formatRelative } from "../lib/format.js";
import { Card, CardHeader, PageHeader } from "../components/ui/Card.jsx";
import { ConfirmDialog } from "../components/ui/Feedback.jsx";
import { ConnectionBadge } from "../components/ui/Badge.jsx";
import Button from "../components/ui/Button.jsx";
import Icon from "../components/ui/Icon.jsx";

const ENDPOINTS = [
  ["GET", "/api/health", "Status, counts, LAN addresses"],
  ["GET", "/api/stats", "Aggregates, per-level breakdown, leaderboard"],
  ["GET", "/api/sessions", "All sessions (filters: player, level, outcome, limit)"],
  ["GET", "/api/sessions/:id", "One session + its events"],
  ["GET", "/api/players", "Per-player summaries"],
  ["GET", "/api/events", "Recent events (filters: type, sessionId, limit)"],
  ["POST", "/api/sessions", "Unity: finished battle (same id = update)"],
  ["POST", "/api/events", "Unity: session_start / hit events"],
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

function Row({ label, children }) {
  return (
    <div className="flex items-center justify-between gap-4 px-5 py-3 text-sm">
      <span className="text-[var(--muted)]">{label}</span>
      <span className="truncate text-right font-medium tabular-nums text-[var(--ink)]">{children}</span>
    </div>
  );
}

export default function SettingsView({ connection, health, checkHealth, pingRoundTrip, refresh, toast }) {
  const [test, setTest] = useState(null);
  const [testing, setTesting] = useState(false);
  const [confirm, setConfirm] = useState(false);
  const [clearing, setClearing] = useState(false);

  const origin = API_BASE || window.location.origin;
  const unityUrl = health?.lanUrls?.[0] || `http://localhost:${health?.port || 4000}`;

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
        description="Check the Node backend is reachable, test the realtime channel, and get the URL to paste into Unity."
        actions={<ConnectionBadge state={connection} />}
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
                <span className="text-[var(--good)]">OK · {health.latencyMs} ms</span>
              ) : (
                <span className="text-[var(--bad)]">{health?.error || "Unreachable"}</span>
              )}
            </Row>
            <Row label="Realtime socket">
              <span className="capitalize">{connection}</span>
            </Row>
            <Row label="Uptime">{health?.ok ? formatDuration(health.uptimeSec) : "—"}</Row>
            <Row label="Stored sessions / events">{health?.ok ? `${health.sessions} / ${health.events}` : "—"}</Row>
            <Row label="Connected dashboards">{health?.ok ? health.clients : "—"}</Row>
          </div>
          <div className="border-t border-[var(--border)] bg-[var(--surface-muted)] px-5 py-4">
            <div className="flex flex-wrap items-center gap-3">
              <Button variant="primary" size="md" icon="signal" onClick={runTest} disabled={testing}>
                {testing ? "Testing…" : "Run connection test"}
              </Button>
              {test?.ok && (
                <span className="flash-in text-sm text-[var(--good)]">
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
              Sends a ping through <code>POST /api/events</code> and waits for it to come back over Socket.io. Pings aren't saved.
            </p>
          </div>
        </Card>

        <Card className="overflow-hidden">
          <CardHeader title="Connect Unity" subtitle="SessionReporter → Backend Base Url" />
          <div className="space-y-5 px-5 py-5">
            <ol className="space-y-4 text-sm text-[var(--ink-secondary)]">
              <li className="flex gap-3">
                <span className="flex h-6 w-6 shrink-0 items-center justify-center rounded-full bg-[var(--accent-soft)] text-xs font-bold text-[var(--accent)]">1</span>
                <div className="min-w-0 flex-1">
                  <p>
                    Start the backend: <code>cd web/backend && npm start</code>
                  </p>
                </div>
              </li>
              <li className="flex gap-3">
                <span className="flex h-6 w-6 shrink-0 items-center justify-center rounded-full bg-[var(--accent-soft)] text-xs font-bold text-[var(--accent)]">2</span>
                <div className="min-w-0 flex-1 space-y-2">
                  <p>
                    In the Unity Editor (Play mode on this Mac) use <code>http://localhost:4000</code>. On the Quest over Wi-Fi, use this Mac&apos;s LAN address:
                  </p>
                  <CopyField value={unityUrl} />
                  {health?.lanUrls?.length > 1 && (
                    <p className="text-xs text-[var(--muted)]">Other addresses: {health.lanUrls.slice(1).join(", ")}</p>
                  )}
                </div>
              </li>
              <li className="flex gap-3">
                <span className="flex h-6 w-6 shrink-0 items-center justify-center rounded-full bg-[var(--accent-soft)] text-xs font-bold text-[var(--accent)]">3</span>
                <p className="min-w-0 flex-1">
                  Paste it into <strong className="font-semibold text-[var(--ink)]">SessionReporter → Backend Base Url</strong> (no trailing slash). Battles show up on the dashboard as soon as they finish.
                </p>
              </li>
            </ol>
            <p className="rounded-lg bg-[var(--warn-soft)] px-3 py-2 text-xs text-[var(--warn)]">
              Headset can&apos;t connect? Both devices must be on the same Wi-Fi, and macOS may ask to allow incoming connections for <code>node</code>.
            </p>
          </div>
        </Card>
      </div>

      <Card className="overflow-hidden">
        <CardHeader title="API reference" subtitle="Everything the backend exposes" />
        <ul className="divide-y divide-[var(--border)]">
          {ENDPOINTS.map(([method, path, desc]) => (
            <li key={`${method} ${path}`} className="flex flex-wrap items-center gap-x-3 gap-y-1 px-5 py-2.5 text-sm">
              <span className={`w-16 rounded-md px-1.5 py-0.5 text-center font-mono text-[0.68rem] font-semibold ${METHOD_CLS[method]}`}>{method}</span>
              <code className="border-0 bg-transparent p-0 text-[var(--ink)]">{path}</code>
              <span className="text-xs text-[var(--muted)] sm:ml-auto">{desc}</span>
            </li>
          ))}
        </ul>
      </Card>

      <Card className="border-red-200">
        <div className="flex flex-wrap items-center justify-between gap-4 px-5 py-4">
          <div>
            <h2 className="text-sm font-semibold text-[var(--ink)]">Clear all data</h2>
            <p className="mt-0.5 text-xs text-[var(--muted)]">Deletes every session and event from db.json. Can&apos;t be undone.</p>
          </div>
          <Button variant="danger" icon="trash" onClick={() => setConfirm(true)} disabled={!health?.ok}>
            Clear database
          </Button>
        </div>
      </Card>

      <ConfirmDialog
        open={confirm}
        title="Clear the whole database?"
        description="Every recorded session and event will be permanently removed from db.json."
        confirmLabel="Clear everything"
        busy={clearing}
        onConfirm={clearAll}
        onCancel={() => setConfirm(false)}
      />
    </div>
  );
}
