import { lazy, Suspense, useState } from "react";
import { api } from "../lib/api.js";
import { levelName } from "../lib/game.js";
import { formatDuration, formatNumber } from "../lib/format.js";
import { formatWinRate, WIN_RATE_RULE } from "../lib/outcomes.js";
import { formatFade } from "../lib/effort.js";
import { levelsText, returnRateText, rpeText } from "../lib/labText.js";
import Icon from "../components/ui/Icon.jsx";
import { useApi } from "../hooks/useApi.js";
import { Card, CardHeader, PageHeader, SectionRule } from "../components/ui/Card.jsx";
import { Banner, EmptyState, Skeleton } from "../components/ui/Feedback.jsx";
import { LabSkeleton } from "../components/ui/Skeletons.jsx";
import { LevelBadge } from "../components/ui/Badge.jsx";
import Button from "../components/ui/Button.jsx";
import { toast } from "../lib/toast.js";
import { SampleBadge, SmallSampleFlag, WhatThis } from "../components/lab/LabParts.jsx";

// Thesis Lab: the figures a thesis chapter needs, in one place, with their sample sizes. Everything comes from GET /api/lab
// (backend/lab.js); the win rate is the shared rule from lib/outcomes.js, the same one the rest of the dashboard uses.
// The scatter plot is a lazy chunk. Nothing here animates.

const RpeScatter = lazy(() => import("../components/lab/RpeScatter.jsx"));
const StudySection = lazy(() => import("../components/lab/StudySection.jsx"));

const NOTE = "Research prototype. Small sample. Correlation is not causation.";

function ReturnRate({ data }) {
  const windows = data.returnRate.windows;
  return (
    <div className="p-5">
      <ul className="grid gap-4 sm:grid-cols-3">
        {windows.map((w) => (
          <li key={w.days} className="rounded-xl border border-[var(--border)] bg-[var(--surface)] p-4">
            <p className="t-kicker">Back within {w.days} day{w.days === 1 ? "" : "s"}</p>
            <p className="mt-3 text-3xl font-extrabold tabular-nums tracking-tight text-[var(--ink)]">{w.ratePct === null ? "—" : `${w.ratePct}%`}</p>
            <p className="mt-1 text-sm font-medium text-[var(--ink-secondary)]">
              {w.n ? `${w.returned} of ${w.n} players` : "No player has been around this long yet"}
            </p>
            <div className="mt-3">
              <SampleBadge n={w.n} small={w.smallSample} />
            </div>
          </li>
        ))}
      </ul>
      <p className="mt-4 text-xs font-medium leading-relaxed text-[var(--muted)]">{data.returnRate.definition}</p>
      <div className="sr-only">
        <table>
        <caption>Return rate by window</caption>
        <thead><tr><th scope="col">Window</th><th scope="col">Players counted (n)</th><th scope="col">Came back</th><th scope="col">Rate</th><th scope="col">Sample</th></tr></thead>
        <tbody>
          {windows.map((w) => (
            <tr key={w.days}><td>{w.days} days</td><td>{w.n}</td><td>{w.returned}</td><td>{w.ratePct === null ? "not available" : `${w.ratePct}%`}</td><td>{w.smallSample ? "small sample" : "ok"}</td></tr>
          ))}
        </tbody>
        </table>
      </div>
    </div>
  );
}

function Rpe({ data }) {
  const rpe = data.rpe;
  if (rpe.status !== "ok") {
    return (
      <EmptyState
        icon="analytics"
        title="Not enough rated fights for a correlation"
        description={`Pearson r needs ${rpe.minPairs} rated fights that also have swing data; there ${rpe.n === 1 ? "is" : "are"} ${rpe.n} so far. Players rate a fight on the feedback page after it ends. Until there are ${rpe.minPairs}, no number or plot is shown.`}
      />
    );
  }
  return (
    <div className="p-5">
      <p className="flex flex-wrap items-baseline gap-x-3 gap-y-1">
        <span className="text-3xl font-extrabold tabular-nums tracking-tight text-[var(--ink)]">r = {rpe.r.toFixed(2)}</span>
        <span className="text-sm font-semibold text-[var(--ink-secondary)]">Pearson, {rpe.n} rated fights</span>
        <SampleBadge n={rpe.n} small={rpe.n < 30} />
      </p>
      <div className="mt-4">
        <Suspense fallback={<Skeleton className="w-full rounded-xl" style={{ height: 280 }} />}>
          <RpeScatter points={rpe.points} r={rpe.r.toFixed(2)} />
        </Suspense>
      </div>
      <p className="mt-3 text-xs font-medium leading-relaxed text-[var(--muted)]">{rpe.definition} {NOTE}</p>
    </div>
  );
}

function LevelTable({ levels }) {
  const dash = (v, f = (x) => x) => (v === null || v === undefined ? "—" : f(v));
  return (
    <div className="scrollbar-thin overflow-x-auto">
      <table className="w-full min-w-[46rem] text-left text-sm">
        <caption className="sr-only">Level-by-level comparison. {WIN_RATE_RULE}</caption>
        <thead>
          <tr className="border-b border-[var(--border)] text-xs text-[var(--muted)]">
            <th scope="col" className="px-5 py-3 font-semibold">Level</th>
            <th scope="col" className="px-3 py-3 text-right font-semibold">Fights</th>
            <th scope="col" className="px-3 py-3 text-right font-semibold">Won / lost</th>
            <th scope="col" className="px-3 py-3 text-right font-semibold">Abandoned</th>
            <th scope="col" className="px-3 py-3 text-right font-semibold">Win rate</th>
            <th scope="col" className="px-3 py-3 text-right font-semibold">Avg score</th>
            <th scope="col" className="px-3 py-3 text-right font-semibold">Avg swing</th>
            <th scope="col" className="px-3 py-3 text-right font-semibold">Avg fade</th>
            <th scope="col" className="px-5 py-3 text-right font-semibold">Avg length</th>
          </tr>
        </thead>
        <tbody className="tabular-nums">
          {levels.map((l) => (
            <tr key={l.level} className="border-b border-[var(--border)] last:border-0">
              <th scope="row" className="px-5 py-3 font-bold text-[var(--ink)]">
                <span className="flex items-center gap-2">
                  <LevelBadge level={l.level} /> {levelName(l.level)}
                </span>
              </th>
              <td className="px-3 py-3 text-right">{formatNumber(l.sessions)}</td>
              <td className="px-3 py-3 text-right">{l.victories} / {l.defeats}</td>
              <td className="px-3 py-3 text-right text-[var(--muted)]">{l.abandoned}</td>
              <td className="px-3 py-3 text-right font-extrabold text-[var(--ink)]">{formatWinRate(l.winRate)}</td>
              <td className="px-3 py-3 text-right">{dash(l.avgScore, formatNumber)}</td>
              <td className="px-3 py-3 text-right">{dash(l.avgSwingSpeed, (v) => `${v.toFixed(1)} m/s`)}</td>
              <td className="px-3 py-3 text-right" title={l.fadeSessions ? `${l.fadeSessions} fights with enough swings` : undefined}>{dash(l.avgFadePct, formatFade)}</td>
              <td className="px-5 py-3 text-right">{dash(l.avgDurationSec, formatDuration)}</td>
            </tr>
          ))}
        </tbody>
      </table>
      <p className="border-t border-[var(--border)] px-5 py-3 text-xs font-medium leading-relaxed text-[var(--muted)]">
        {WIN_RATE_RULE} Average score is over decided fights. Swing speeds above the game's 8 m/s cap count as 8. Fade needs 6+ swings per fight.
      </p>
    </div>
  );
}

function ExportCsv() {
  const [status, setStatus] = useState(null);
  const run = async () => {
    setStatus({ state: "loading" });
    try {
      const res = await fetch(api.labCsvUrl());
      if (!res.ok) throw new Error(`the server answered ${res.status}`);
      const blob = await res.blob();
      const rows = Number(res.headers.get("X-Row-Count"));
      const name = /filename="([^"]+)"/.exec(res.headers.get("Content-Disposition") || "")?.[1] || "strikefit-lab-sessions.csv";
      const url = URL.createObjectURL(blob);
      const a = Object.assign(document.createElement("a"), { href: url, download: name });
      document.body.appendChild(a);
      a.click();
      a.remove();
      setTimeout(() => URL.revokeObjectURL(url), 5000);
      setStatus({ state: "done", rows, name });
      toast.good("CSV downloaded", `${name}${Number.isFinite(rows) ? ` · ${rows.toLocaleString()} rows` : ""}`);
    } catch (err) {
      const message = err.message === "Failed to fetch" ? "the server didn't answer" : err.message;
      setStatus({ state: "error", message });
      toast.bad("Export failed", message);
    }
  };
  return (
    <div className="flex flex-col items-end gap-1">
      <Button variant="primary" size="md" icon={status?.state === "loading" ? "loader" : "download"} onClick={run} disabled={status?.state === "loading"}>
        {status?.state === "loading" ? "Preparing…" : "Export CSV"}
      </Button>
      <p className="text-xs font-medium text-[var(--muted)]" role="status">
        {status?.state === "done" && `${formatNumber(status.rows)} sessions saved as ${status.name}`}
        {status?.state === "error" && <span className="text-[var(--bad)]">The download didn't finish: {status.message}. Try again in a moment.</span>}
      </p>
    </div>
  );
}

export default function LabView() {
  const { data, error, reload } = useApi(() => api.lab(), []);
  return (
    <div className="space-y-6">
      <PageHeader
        eyebrow="Research"
        title="Thesis Lab"
        description="Return rate, perceived exertion against swing speed, and a level-by-level comparison, each with the number of players or fights behind it."
        actions={<ExportCsv />}
      />
      <Banner tone={data && data.sample.players < 10 ? "warn" : "info"} title={NOTE}>
        {data && data.sample.players < 10 ? (
          <>
            <strong>Small sample: {data.sample.players} players, {formatNumber(data.sample.sessions)} fights.</strong> Every figure below says how many players or fights it rests on; with this few they describe this group, not people in general.
          </>
        ) : (
          "Every figure below says how many players or fights it rests on. With a handful of players these are descriptions of this group, not findings about people in general."
        )}
      </Banner>

      {error && !data ? (
        <Card>
          <EmptyState icon="clock" title="The Lab will appear here" description="It loads as soon as the server answers." action={<Button onClick={reload}>Try again</Button>} />
        </Card>
      ) : !data ? (
        <LabSkeleton />
      ) : (
        <>
          <dl className="grid grid-cols-2 gap-3 rounded-2xl border border-[var(--border)] bg-[var(--surface)] p-4 shadow-[var(--shadow-card)] sm:grid-cols-4">
            {[
              ["Fights recorded", data.sample.sessions],
              ["Players", data.sample.players],
              ["With swing data", data.sample.sessionsWithSwingData],
              ["Rated by the player", data.sample.sessionsRated],
            ].map(([k, v]) => (
              <div key={k}>
                <dt className="text-xs font-semibold text-[var(--muted)]">{k}</dt>
                <dd className="mt-0.5 text-2xl font-extrabold tabular-nums tracking-tight text-[var(--ink)]">{formatNumber(v)}</dd>
              </div>
            ))}
          </dl>

          <SectionRule index="01" title="Return rate" />
          <Card className="overflow-hidden">
            <CardHeader
              title="Did players come back?"
              subtitle="Share of players who played again within 1, 3 and 7 days of their first fight"
              actions={<SmallSampleFlag n={data.returnRate.windows[0]?.n ?? 0} unit="players" />}
            />
            <ReturnRate data={data} />
            <WhatThis>{returnRateText(data.returnRate)}</WhatThis>
          </Card>

          <SectionRule index="02" title="Perceived vs measured effort" />
          <Card className="overflow-hidden">
            <CardHeader title="Borg RPE against average swing speed" subtitle="Does how hard it felt follow how fast they swung?" actions={<SmallSampleFlag n={data.rpe.n} unit="rated fights" />} />
            <Rpe data={data} />
            <WhatThis>{rpeText(data.rpe)}</WhatThis>
          </Card>

          <SectionRule index="03" title="Levels" />
          <Card className="overflow-hidden">
            <CardHeader
              title="Level-by-level comparison"
              subtitle="Same rule everywhere: wins ÷ (wins + losses), abandoned fights shown apart"
              actions={<SmallSampleFlag n={data.levels.reduce((a, l) => a + l.decided, 0)} unit="decided fights" />}
            />
            <LevelTable levels={data.levels} />
            <WhatThis>{levelsText(data.levels.map((l) => ({ ...l, label: levelName(l.level) })))}</WhatThis>
          </Card>

          {data.study && (
            <Suspense fallback={<Skeleton className="h-72 w-full rounded-2xl" />}>
              <StudySection study={data.study} index="04" />
            </Suspense>
          )}
          <p className="text-center text-xs font-semibold text-[var(--muted)]">{NOTE} Names are pseudonyms; the CSV carries no IP address or device information.</p>
        </>
      )}
    </div>
  );
}
