import { lazy, Suspense, useState } from "react";
import { api } from "../../lib/api.js";
import { formatFade, speakFade } from "../../lib/effort.js";
import { enjoymentText, fadeText, playAgainText, rpeText, susText } from "../../lib/labText.js";
import { toast } from "../../lib/toast.js";
import { formatNumber } from "../../lib/format.js";
import { Card, CardHeader, SectionRule } from "../ui/Card.jsx";
import { EmptyState, Skeleton } from "../ui/Feedback.jsx";
import Button from "../ui/Button.jsx";
import { SampleBadge, SmallSampleFlag, WhatThis } from "./LabParts.jsx";

// The Thesis Lab's playtest-study section: SUS, enjoyment, perceived effort against swing speed, intent to play again, and fade
// per participant. Every chart carries its sample size, the small-sample flag and a "what this tells us" sentence, and has a text
// or table version for screen readers. Nothing animates. Data: GET /api/lab -> study (backend/studyCore.js buildStudy).

const RpeScatter = lazy(() => import("./RpeScatter.jsx"));

const SHORT = { "Below average": "Below avg", "Best imaginable": "Best" };
const BAND_FILL = ["#fee2e2", "#fef3c7", "#dbeafe", "#d1fae5", "#a7f3d0"]; // poor .. best imaginable, light tints under the dots

function SusChart({ sus }) {
  const W = 960, H = 130, PAD = 16;
  const x = (v) => PAD + (v / 100) * (W - 2 * PAD);
  const rows = sus.scores.filter((s) => typeof s.score === "number");
  // dots with the same score stack upward so none hides another
  const seen = new Map();
  const dots = rows.map((s) => {
    const k = Math.round(s.score);
    const level = seen.get(k) || 0;
    seen.set(k, level + 1);
    return { ...s, cx: x(s.score), cy: 76 - level * 15 };
  });
  return (
    <div>
      <svg viewBox={`0 0 ${W} ${H}`} className="block w-full" role="img" aria-label={`SUS scores of ${rows.length} participants on a 0 to 100 scale; the average is ${sus.mean}. A table follows.`}>
        {sus.bands.map((b, i) => (
          <rect key={b.label} x={x(b.from)} y="22" width={x(Math.min(b.to, 100)) - x(b.from)} height="64" fill={BAND_FILL[i]} />
        ))}
        {sus.bands.map((b) => (
          <text key={b.label} x={(x(b.from) + x(Math.min(b.to, 100))) / 2} y="14" textAnchor="middle" fontSize="12" fontWeight="700" fill="var(--muted)">
            {SHORT[b.label] || b.label}
          </text>
        ))}
        <line x1={x(68)} x2={x(68)} y1="18" y2="94" stroke="var(--ink)" strokeDasharray="3 3" />
        <text x={x(68) - 6} y="104" textAnchor="end" fontSize="12" fontWeight="700" fill="var(--ink)">68 = usual benchmark</text>
        {typeof sus.mean === "number" && <path d={`M${x(sus.mean)} 90 l-6 9 h12 z`} fill="var(--accent-strong)"><title>{`Average ${sus.mean}`}</title></path>}
        {dots.map((d) => (
          <circle key={d.code} cx={d.cx} cy={d.cy} r="7" fill="var(--accent)" stroke="var(--surface)" strokeWidth="1.5">
            <title>{`${d.code}: ${d.score}`}</title>
          </circle>
        ))}
        {[0, 25, 50, 75, 100].map((v) => (
          <text key={v} x={x(v)} y="126" textAnchor="middle" fontSize="12" fill="var(--muted)" className="tabular-nums">{v}</text>
        ))}
      </svg>
      <details className="mt-2 rounded-lg border border-[var(--border)] bg-[var(--surface-muted)] px-3 py-2 text-xs">
        <summary className="cursor-pointer font-bold text-[var(--accent)]">Show the scores as a table</summary>
        <table className="mt-2 w-full text-left tabular-nums">
          <caption className="sr-only">SUS score per participant</caption>
          <thead><tr><th scope="col" className="py-1 pr-4">Participant</th><th scope="col" className="py-1 pr-4">SUS score</th><th scope="col" className="py-1">Band</th></tr></thead>
          <tbody>{sus.scores.map((s) => <tr key={s.code}><th scope="row" className="py-1 pr-4 font-bold">{s.code}</th><td className="py-1 pr-4">{s.score}</td><td className="py-1">{s.band}</td></tr>)}</tbody>
        </table>
      </details>
    </div>
  );
}

const ITEM_LABELS = ["Enjoyed playing", "Swinging felt satisfying", "Time passed quickly", "Felt like trying hard", "Would recommend"];

function Enjoyment({ en }) {
  return (
    <ul className="space-y-3 p-5" aria-label="Average rating per statement, 1 to 5">
      {en.items.map((v, i) => (
        <li key={ITEM_LABELS[i]} className="grid grid-cols-[minmax(0,11rem)_minmax(0,1fr)_2.5rem] items-center gap-3 text-sm">
          <span className="font-semibold text-[var(--ink-secondary)]">{ITEM_LABELS[i]}</span>
          <span className="h-2.5 overflow-hidden rounded-full bg-[var(--surface-muted)]" aria-hidden>
            <span className="block h-full origin-left rounded-full bg-[var(--accent)]" style={{ transform: `scaleX(${typeof v === "number" ? (v - 1) / 4 : 0})` }} />
          </span>
          <span className="text-right font-extrabold tabular-nums text-[var(--ink)]">{typeof v === "number" ? v.toFixed(1) : "—"}</span>
        </li>
      ))}
    </ul>
  );
}

function PlayAgain({ pa }) {
  const parts = [["yes", "Yes", "var(--good)"], ["maybe", "Maybe", "var(--warn)"], ["no", "No", "var(--bad)"]];
  return (
    <div className="p-5">
      <div className="flex h-9 overflow-hidden rounded-full bg-[var(--surface-muted)]" role="img" aria-label={`Would play again: ${pa.yes} yes, ${pa.maybe} maybe, ${pa.no} no, of ${pa.n}`}>
        {parts.map(([k, , c]) => (pa[k] ? <span key={k} style={{ width: `${(pa[k] / pa.n) * 100}%`, background: c }} className="h-full" /> : null))}
      </div>
      <ul className="mt-3 flex flex-wrap gap-x-5 gap-y-1 text-sm font-semibold text-[var(--ink-secondary)]">
        {parts.map(([k, label, c]) => (
          <li key={k} className="inline-flex items-center gap-2"><span className="h-2.5 w-2.5 rounded-full" style={{ background: c }} aria-hidden />{label}: <span className="tabular-nums font-extrabold text-[var(--ink)]">{pa[k]}</span></li>
        ))}
      </ul>
    </div>
  );
}

function PerParticipant({ rows }) {
  const max = Math.max(30, ...rows.map((r) => Math.abs(r.fadePct ?? 0)));
  return (
    <div className="scrollbar-thin overflow-x-auto">
      <table className="w-full min-w-[38rem] text-left text-sm">
        <caption className="sr-only">Per participant: fights, average swing speed, effort fade between the first and last third of a fight, Borg RPE, SUS score</caption>
        <thead>
          <tr className="border-b border-[var(--border)] text-xs text-[var(--muted)]">
            <th scope="col" className="px-5 py-3 font-semibold">Participant</th>
            <th scope="col" className="px-3 py-3 text-right font-semibold">Fights</th>
            <th scope="col" className="px-3 py-3 text-right font-semibold">Avg swing</th>
            <th scope="col" className="px-3 py-3 font-semibold">Fade (last third vs first)</th>
            <th scope="col" className="px-3 py-3 text-right font-semibold">RPE</th>
            <th scope="col" className="px-5 py-3 text-right font-semibold">SUS</th>
          </tr>
        </thead>
        <tbody className="tabular-nums">
          {rows.map((r) => (
            <tr key={r.code} className="border-b border-[var(--border)] last:border-0">
              <th scope="row" className="px-5 py-2.5 font-bold text-[var(--ink)]">{r.code}</th>
              <td className="px-3 py-2.5 text-right">{r.sessions}</td>
              <td className="px-3 py-2.5 text-right">{r.avgSpeed === null ? "—" : `${r.avgSpeed.toFixed(1)} m/s`}</td>
              <td className="px-3 py-2.5">
                {r.fadePct === null ? (
                  "—"
                ) : (
                  <span className="flex items-center gap-2" aria-label={speakFade(r.fadePct)}>
                    {/* a bar from the middle: left = slower at the end, right = faster at the end */}
                    <span className="relative h-2 w-28 shrink-0 rounded-full bg-[var(--surface-muted)]" aria-hidden>
                      <span className="absolute inset-y-0 left-1/2 w-px bg-[var(--border-strong)]" />
                      <span
                        className={`absolute inset-y-0 rounded-full ${r.fadePct < 0 ? "bg-[var(--warn)]" : "bg-[var(--good)]"}`}
                        style={{ width: `${(Math.abs(r.fadePct) / max) * 50}%`, [r.fadePct < 0 ? "right" : "left"]: "50%" }}
                      />
                    </span>
                    <span className="w-12 font-bold text-[var(--ink)]">{formatFade(r.fadePct)}</span>
                  </span>
                )}
              </td>
              <td className="px-3 py-2.5 text-right">{r.rpe === null ? "—" : r.rpe}</td>
              <td className="px-5 py-2.5 text-right font-bold text-[var(--ink)]">{r.sus === null ? "—" : r.sus}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

function ExportStudyCsv() {
  const [busy, setBusy] = useState(false);
  const run = async () => {
    setBusy(true);
    try {
      const res = await fetch(api.studyCsvUrl());
      if (!res.ok) throw new Error(`the server answered ${res.status}`);
      const blob = await res.blob();
      const rows = Number(res.headers.get("X-Row-Count"));
      const name = /filename="([^"]+)"/.exec(res.headers.get("Content-Disposition") || "")?.[1] || "strikefit-study.csv";
      const url = URL.createObjectURL(blob);
      const a = Object.assign(document.createElement("a"), { href: url, download: name });
      document.body.appendChild(a);
      a.click();
      a.remove();
      setTimeout(() => URL.revokeObjectURL(url), 5000);
      toast.good("Study CSV downloaded", `${name}${Number.isFinite(rows) ? ` · ${formatNumber(rows)} rows` : ""}`);
    } catch (err) {
      toast.bad("Export failed", err.message === "Failed to fetch" ? "the server didn't answer" : err.message);
    } finally {
      setBusy(false);
    }
  };
  return (
    <Button icon={busy ? "loader" : "download"} onClick={run} disabled={busy}>
      {busy ? "Preparing…" : "Export study CSV"}
    </Button>
  );
}

export default function StudySection({ study, index = "04" }) {
  if (!study) return null;
  const { sample, sus, enjoyment, playAgain, effortVsUsual, rpe, perParticipant } = study;
  return (
    <>
      <SectionRule index={index} title="Playtest study" aside={<ExportStudyCsv />} />
      <dl className="grid grid-cols-2 gap-3 rounded-2xl border border-[var(--border)] bg-[var(--surface)] p-4 shadow-[var(--shadow-card)] sm:grid-cols-4">
        {[
          ["Participants (consented)", sample.participants],
          ["Played at least one fight", sample.withFights],
          ["Finished the questionnaire", sample.completed],
          ["Effort vs usual workout", effortVsUsual.mean === null ? "—" : `${effortVsUsual.mean.toFixed(1)} / 5`],
        ].map(([k, v]) => (
          <div key={k}>
            <dt className="text-xs font-semibold text-[var(--muted)]">{k}</dt>
            <dd className="mt-0.5 text-2xl font-extrabold tabular-nums tracking-tight text-[var(--ink)]">{typeof v === "number" ? formatNumber(v) : v}</dd>
          </div>
        ))}
      </dl>

      <Card className="overflow-hidden">
        <CardHeader title="System Usability Scale (SUS)" subtitle="Ten statements about using the game, scored 0 to 100; 68 is the usual benchmark" actions={<SmallSampleFlag always n={sus.n} unit="participants" />} />
        {sus.n ? (
          <div className="p-5">
            <p className="flex flex-wrap items-baseline gap-x-3 gap-y-1">
              <span className="text-3xl font-extrabold tabular-nums tracking-tight text-[var(--ink)]">{sus.mean.toFixed(1)}</span>
              <span className="text-sm font-semibold text-[var(--ink-secondary)]">average · {sus.band?.label}</span>
              <SampleBadge n={sus.n} small />
            </p>
            <div className="mt-4"><SusChart sus={sus} /></div>
          </div>
        ) : (
          <EmptyState icon="analytics" title="No SUS scores yet" description="They appear once a participant finishes the after-play questionnaire on the study page." />
        )}
        <WhatThis>{susText(sus)}</WhatThis>
      </Card>

      <div className="grid gap-6 xl:grid-cols-2">
        <Card className="overflow-hidden">
          <CardHeader title="Enjoyment" subtitle="Average of five statements, 1 (strongly disagree) to 5 (strongly agree)" actions={<SmallSampleFlag always n={enjoyment.n} unit="participants" />} />
          {enjoyment.n ? (
            <>
              <p className="px-5 pt-5 text-3xl font-extrabold tabular-nums tracking-tight text-[var(--ink)]">{enjoyment.mean.toFixed(2)} <span className="text-base font-semibold text-[var(--muted)]">out of 5</span></p>
              <Enjoyment en={enjoyment} />
            </>
          ) : (
            <EmptyState icon="analytics" title="No ratings yet" />
          )}
          <WhatThis>{enjoymentText(enjoyment)}</WhatThis>
        </Card>
        <Card className="overflow-hidden">
          <CardHeader title="Would play again this week" subtitle="Stated intent after the fight" actions={<SmallSampleFlag always n={playAgain.n} unit="participants" />} />
          {playAgain.n ? (
            <>
              <p className="px-5 pt-5 text-3xl font-extrabold tabular-nums tracking-tight text-[var(--ink)]">{playAgain.yesPct}% <span className="text-base font-semibold text-[var(--muted)]">said yes</span></p>
              <PlayAgain pa={playAgain} />
            </>
          ) : (
            <EmptyState icon="analytics" title="No answers yet" />
          )}
          <WhatThis>{playAgainText(playAgain)}</WhatThis>
        </Card>
      </div>

      <Card className="overflow-hidden">
        <CardHeader title="Perceived effort against swing speed (study participants)" subtitle="Borg RPE after the fight against that fight's average swing speed" actions={<SmallSampleFlag always n={rpe.n} unit="rated fights" />} />
        {rpe.status === "ok" ? (
          <div className="p-5">
            <p className="flex flex-wrap items-baseline gap-x-3 gap-y-1">
              <span className="text-3xl font-extrabold tabular-nums tracking-tight text-[var(--ink)]">r = {rpe.r.toFixed(2)}</span>
              <span className="text-sm font-semibold text-[var(--ink-secondary)]">Pearson, {rpe.n} rated fights</span>
              <SampleBadge n={rpe.n} small />
            </p>
            <div className="mt-4">
              <Suspense fallback={<Skeleton className="w-full rounded-xl" style={{ height: 280 }} />}>
                <RpeScatter points={rpe.points} r={rpe.r.toFixed(2)} />
              </Suspense>
            </div>
          </div>
        ) : (
          <EmptyState icon="analytics" title="Not enough rated fights for a correlation" description={`Pearson r needs ${rpe.minPairs} rated fights with swing data; study participants have ${rpe.n} so far. No number or plot is shown until then.`} />
        )}
        <WhatThis>{rpeText(rpe)}</WhatThis>
      </Card>

      <Card className="overflow-hidden">
        <CardHeader title="Each participant" subtitle="Fade is the change in swing speed from the first to the last third of a fight" actions={<SmallSampleFlag always n={perParticipant.filter((p) => p.fadePct !== null).length} unit="participants" />} />
        <PerParticipant rows={perParticipant} />
        <WhatThis>{fadeText(perParticipant)}</WhatThis>
      </Card>
    </>
  );
}
