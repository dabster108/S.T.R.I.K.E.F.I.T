import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { io } from "socket.io-client";

const API_BASE = (import.meta.env.VITE_API_URL || "").trim();

function formatTime(iso) {
  if (!iso) return "—";
  try {
    return new Date(iso).toLocaleString();
  } catch {
    return iso;
  }
}

function formatShortDate(iso) {
  if (!iso) return "—";
  try {
    return new Date(iso).toLocaleDateString(undefined, { month: "short", day: "numeric" });
  } catch {
    return "—";
  }
}

function formatEventTime(iso) {
  if (!iso) return "—";
  try {
    return new Date(iso).toLocaleTimeString(undefined, {
      hour: "2-digit",
      minute: "2-digit",
      second: "2-digit",
    });
  } catch {
    return iso;
  }
}

const card = "rounded-2xl border border-[var(--border)] bg-[var(--surface)] shadow-sm";
const sectionHead =
  "flex flex-wrap items-center justify-between gap-2 border-b border-[var(--border)] px-5 py-4";
const sectionTitle = "text-sm font-semibold text-[var(--ink)]";
const sectionSubtitle = "text-xs text-[var(--muted)]";
const thClass =
  "px-4 py-3 text-left text-[0.7rem] font-medium uppercase tracking-wide text-[var(--muted)]";
const tdClass = "px-4 py-2.5 text-sm text-[var(--ink)] whitespace-nowrap tabular-nums";
const controlSelect =
  "rounded-lg border border-[var(--border)] bg-[var(--surface)] px-3 py-1.5 text-xs text-[var(--ink)] shadow-sm";
const controlButton =
  "rounded-lg border border-[var(--border)] bg-[var(--surface)] px-3 py-1.5 text-xs font-medium text-[var(--muted)] shadow-sm transition-colors hover:border-gray-300 hover:text-[var(--ink)] focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-1 focus-visible:outline-[var(--accent)]";

const NAV = [
  { id: "overview", label: "Dashboard" },
  { id: "analytics", label: "Analytics" },
  { id: "sessions", label: "Sessions" },
  { id: "events", label: "Events" },
  { id: "leaderboard", label: "Leaderboard" },
];

export default function App() {
  const [stats, setStats] = useState(null);
  const [sessions, setSessions] = useState([]);
  const [events, setEvents] = useState([]);
  const [liveFlash, setLiveFlash] = useState(null);
  const [health, setHealth] = useState({ ok: false });
  const [range, setRange] = useState("all");
  const [loading, setLoading] = useState(true);
  const [activeNav, setActiveNav] = useState("overview");
  const [navSlider, setNavSlider] = useState({ width: 0, left: 0 });
  const navBtnRefs = useRef([]);
  const apiPrefix = API_BASE || "";

  useEffect(() => {
    let cancelled = false;

    async function bootstrap() {
      try {
        const [sRes, sessRes, evRes] = await Promise.all([
          fetch(`${apiPrefix}/api/stats`),
          fetch(`${apiPrefix}/api/sessions`),
          fetch(`${apiPrefix}/api/events?limit=60`),
        ]);
        if (cancelled) return;
        if (sRes.ok) setStats(await sRes.json());
        if (sessRes.ok) {
          const data = await sessRes.json();
          setSessions(data.sessions || []);
        }
        if (evRes.ok) {
          const data = await evRes.json();
          setEvents(data.events || []);
        }
      } catch (err) {
        console.warn("API bootstrap failed (is backend running?)", err);
      } finally {
        if (!cancelled) setLoading(false);
      }
    }

    async function checkHealth() {
      try {
        const res = await fetch(`${apiPrefix}/api/health`);
        if (!res.ok) throw new Error("Health check failed");
        const payload = await res.json();
        void payload;
        if (!cancelled) setHealth({ ok: true });
      } catch {
        if (!cancelled) setHealth({ ok: false });
      }
    }

    bootstrap();
    checkHealth();
    const healthTimer = setInterval(checkHealth, 12000);

    const socket = io(API_BASE || undefined, {
      transports: ["websocket", "polling"],
      timeout: 6000,
    });

    socket.on("stats", (payload) => setStats(payload));
    socket.on("sessions", (list) => setSessions(Array.isArray(list) ? list : []));
    socket.on("events", (list) => setEvents(Array.isArray(list) ? list : []));

    socket.on("session", (session) => {
      setSessions((prev) => [session, ...prev.filter((s) => s.id !== session.id)]);
      setLiveFlash({
        title: session.outcome === "victory" ? "Victory" : session.outcome === "defeat" ? "Defeat" : "Session",
        detail: `${session.playerName} · ${session.score} pts · ${session.hitCount || 0} hits`,
      });
      setTimeout(() => setLiveFlash(null), 3500);
    });

    socket.on("event", (event) => {
      setEvents((prev) => [event, ...prev].slice(0, 80));
    });

    socket.on("cleared", () => {
      setSessions([]);
      setEvents([]);
      setStats({
        totalSessions: 0,
        victories: 0,
        defeats: 0,
        totalScore: 0,
        totalDamage: 0,
        totalHits: 0,
        totalTaps: 0,
        bestScore: 0,
        winRate: 0,
        leaderboard: [],
      });
    });

    return () => {
      cancelled = true;
      clearInterval(healthTimer);
      socket.disconnect();
    };
  }, [apiPrefix]);

  async function clearAll() {
    if (!confirm("Clear all tracked sessions from db.json?")) return;
    await fetch(`${apiPrefix}/api/sessions`, { method: "DELETE" });
  }

  const s = stats || {};
  const filteredSessions = useMemo(() => {
    if (range === "all") return sessions;
    const now = Date.now();
    const cutoffMs = range === "24h" ? 24 * 60 * 60 * 1000 : 7 * 24 * 60 * 60 * 1000;
    return sessions.filter((item) => {
      const ts = new Date(item.endedAt || item.startedAt || 0).getTime();
      return Number.isFinite(ts) && now - ts <= cutoffMs;
    });
  }, [sessions, range]);

  const winRate = Number(s.winRate || 0);
  const recentScores = useMemo(
    () => filteredSessions.slice(0, 12).reverse(),
    [filteredSessions]
  );
  const sessionMomentum = filteredSessions.slice(0, 6);
  const apiOnline = health.ok;

  const updateNavSlider = useCallback(() => {
    const idx = NAV.findIndex((n) => n.id === activeNav);
    const el = navBtnRefs.current[idx];
    const parent = el?.parentElement;
    if (!el || !parent) return;
    setNavSlider({ width: el.offsetWidth, left: el.offsetLeft });
  }, [activeNav]);

  useEffect(() => {
    updateNavSlider();
    window.addEventListener("resize", updateNavSlider);
    return () => window.removeEventListener("resize", updateNavSlider);
  }, [updateNavSlider, loading]);

  useEffect(() => {
    if (loading) return undefined;
    const sections = NAV.map((n) => document.getElementById(n.id)).filter(Boolean);
    const observer = new IntersectionObserver(
      (entries) => {
        const hit = entries
          .filter((e) => e.isIntersecting)
          .sort((a, b) => b.intersectionRatio - a.intersectionRatio)[0];
        if (hit?.target?.id) setActiveNav(hit.target.id);
      },
      { rootMargin: "-30% 0px -55% 0px", threshold: [0.08, 0.2, 0.4] }
    );
    sections.forEach((el) => observer.observe(el));
    return () => observer.disconnect();
  }, [loading, stats]);

  useEffect(() => {
    if (loading) return undefined;
    const nodes = document.querySelectorAll("[data-reveal]");
    const reveal = new IntersectionObserver(
      (entries) => {
        entries.forEach((entry) => {
          if (entry.isIntersecting) entry.target.classList.add("is-visible");
        });
      },
      { threshold: 0.12 }
    );
    nodes.forEach((node) => reveal.observe(node));
    requestAnimationFrame(() => {
      nodes.forEach((node) => {
        const rect = node.getBoundingClientRect();
        if (rect.top < window.innerHeight * 0.9) node.classList.add("is-visible");
      });
    });
    return () => reveal.disconnect();
  }, [loading, stats]);

  function scrollToSection(id) {
    setActiveNav(id);
    const el = document.getElementById(id);
    if (el) el.scrollIntoView({ behavior: "smooth", block: "start" });
  }

  return (
    <div className="min-h-screen bg-[var(--bg)] text-[var(--ink)]">
      <header className="sticky top-0 z-20 border-b border-[var(--border)] bg-[var(--surface)]/90 backdrop-blur-md">
        <div className="mx-auto flex max-w-6xl flex-col gap-3 px-4 py-3 md:flex-row md:items-center md:justify-between">
          <button
            type="button"
            onClick={() => scrollToSection("overview")}
            className="group flex items-center gap-2.5 rounded-xl text-left transition-transform active:scale-[0.98]"
          >
            <div className="flex h-10 w-10 items-center justify-center rounded-xl bg-[var(--accent)] text-sm font-bold text-white shadow-sm transition-shadow group-hover:shadow-md">
              FM
            </div>
            <div>
              <p className="text-sm font-semibold text-[var(--ink)]">Monster Battle</p>
              <p className="text-xs text-[var(--muted)]">VR combat telemetry</p>
            </div>
          </button>

          <nav
            className="relative flex w-full gap-0.5 overflow-x-auto rounded-full border border-[var(--border)] bg-[var(--surface-muted)] p-1 scrollbar-thin md:w-auto"
            aria-label="Page sections"
          >
            <span
              className="nav-slider pointer-events-none absolute bottom-1 top-1 rounded-full bg-[var(--surface)] shadow-sm"
              style={{
                width: navSlider.width ? `${navSlider.width}px` : 0,
                transform: `translateX(${navSlider.left}px)`,
              }}
              aria-hidden
            />
            {NAV.map((item, index) => (
              <button
                key={item.id}
                ref={(el) => {
                  navBtnRefs.current[index] = el;
                }}
                type="button"
                onClick={() => scrollToSection(item.id)}
                className={`relative z-10 shrink-0 rounded-full px-3.5 py-2 text-xs font-medium transition-colors sm:px-4 sm:text-sm ${
                  activeNav === item.id ? "text-[var(--accent)]" : "text-[var(--muted)] hover:text-[var(--ink)]"
                }`}
              >
                {item.label}
              </button>
            ))}
          </nav>
        </div>
      </header>

      <main className="mx-auto max-w-6xl space-y-8 px-4 py-6 sm:px-6 sm:py-8">
          {liveFlash && (
            <div
              className="flash-in flex flex-wrap items-baseline gap-x-3 gap-y-1 rounded-2xl border border-emerald-200 bg-[var(--good-soft)] px-4 py-3 text-sm"
              role="status"
            >
              <span className="font-semibold text-[var(--good)]">{liveFlash.title}</span>
              <span className="text-[var(--muted)]">{liveFlash.detail}</span>
            </div>
          )}

          {loading && !stats ? (
            <div className={`${card} px-6 py-12 text-center text-sm text-[var(--muted)]`}>
              Loading telemetry…
            </div>
          ) : (
            <>
              <section id="overview" data-reveal className="reveal-section scroll-mt-28">
                <HeroOverview stats={s} winRate={winRate} sessionCount={filteredSessions.length} />
                <div className="mt-4 grid gap-4 sm:grid-cols-2 xl:grid-cols-4">
                  <HighlightCard
                    label="Sessions"
                    value={s.totalSessions ?? 0}
                    hint="Total recorded combat runs"
                    tone="blue"
                  />
                  <HighlightCard
                    label="Best score"
                    value={s.bestScore ?? 0}
                    hint="Highest single-session score"
                    tone="indigo"
                  />
                  <HighlightCard
                    label="Win rate"
                    value={`${s.winRate ?? 0}%`}
                    hint={`${s.victories ?? 0}W · ${s.defeats ?? 0}L`}
                    tone="green"
                  />
                  <HighlightCard
                    label="Damage dealt"
                    value={Math.round(s.totalDamage ?? 0)}
                    hint="Cumulative combat damage"
                    tone="amber"
                  />
                </div>
              </section>

              <section id="analytics" data-reveal className="reveal-section scroll-mt-28 space-y-4">
                <div>
                  <h2 className="text-lg font-semibold text-[var(--ink)]">Analytics</h2>
                  <p className="text-sm text-[var(--muted)]">Combat metrics from your VR sessions</p>
                </div>
                <AnalyticsGrid stats={s} />

                <div className={`${card} overflow-hidden`}>
                  <div className="grid lg:grid-cols-12">
                    <div className="lg:col-span-8 lg:border-r lg:border-[var(--border)]">
                      <div className={sectionHead}>
                        <div>
                          <h3 className={sectionTitle}>Score trend</h3>
                          <p className={sectionSubtitle}>Recent sessions over time</p>
                        </div>
                      </div>
                      <div className="px-5 pb-5 pt-1">
                        <ScoreTrendChart sessions={recentScores} />
                      </div>
                    </div>
                    <div className="border-t border-[var(--border)] lg:col-span-4 lg:border-t-0">
                      <div className={sectionHead}>
                        <h3 className={sectionTitle}>Performance</h3>
                      </div>
                      <WinRatePanel
                        winRate={winRate}
                        victories={s.victories ?? 0}
                        defeats={s.defeats ?? 0}
                      />
                      <div className="border-t border-[var(--border)]">
                        <div className={sectionHead}>
                          <h3 className={sectionTitle}>Recent momentum</h3>
                        </div>
                        <MomentumPanel rows={sessionMomentum} />
                      </div>
                    </div>
                  </div>
                </div>
              </section>

              <section id="sessions" data-reveal className={`${card} reveal-section scroll-mt-28 overflow-hidden`}>
                <div className={sectionHead}>
                  <div>
                    <h2 className={sectionTitle}>Recent sessions</h2>
                    <p className={sectionSubtitle}>Full session history and outcomes</p>
                  </div>
                  <div className="flex flex-wrap items-center gap-2">
                    <select
                      value={range}
                      onChange={(e) => setRange(e.target.value)}
                      aria-label="Session range"
                      className={controlSelect}
                    >
                      <option value="all">All time</option>
                      <option value="24h">Last 24h</option>
                      <option value="7d">Last 7 days</option>
                    </select>
                    <button type="button" className={controlButton} onClick={clearAll}>
                      Clear DB
                    </button>
                  </div>
                </div>
                <SessionsTable rows={filteredSessions} />
              </section>

              <div className="grid gap-6 xl:grid-cols-2">
                <section id="events" data-reveal className={`${card} reveal-section scroll-mt-28 overflow-hidden`}>
                  <div className={sectionHead}>
                    <div>
                      <h2 className={sectionTitle}>Live event feed</h2>
                      <p className={sectionSubtitle}>{events.length} events in buffer</p>
                    </div>
                  </div>
                  <EventFeed events={events} apiOnline={apiOnline} />
                </section>

                <section id="leaderboard" data-reveal className={`${card} reveal-section scroll-mt-28 overflow-hidden`}>
                  <div className={sectionHead}>
                    <div>
                      <h2 className={sectionTitle}>Leaderboard</h2>
                      <p className={sectionSubtitle}>Top scores across all sessions</p>
                    </div>
                  </div>
                  <LeaderboardTable rows={s.leaderboard || []} />
                </section>
              </div>
            </>
          )}

          <footer className="pb-6 text-center text-xs text-[var(--muted)]">
            Fitness Monster Battle · session analytics
          </footer>
        </main>
    </div>
  );
}

const TONE_STYLES = {
  blue: { icon: "bg-blue-100 text-blue-600", ring: "stroke-blue-600" },
  indigo: { icon: "bg-indigo-100 text-indigo-600", ring: "stroke-indigo-600" },
  green: { icon: "bg-emerald-100 text-emerald-600", ring: "stroke-emerald-600" },
  amber: { icon: "bg-amber-100 text-amber-600", ring: "stroke-amber-600" },
  rose: { icon: "bg-rose-100 text-rose-600", ring: "stroke-rose-600" },
  slate: { icon: "bg-gray-100 text-gray-600", ring: "stroke-gray-500" },
};

function HeroOverview({ stats, winRate, sessionCount }) {
  const total = stats.totalSessions ?? 0;
  return (
    <div className={`${card} p-6 sm:p-8`}>
      <p className="text-sm font-medium text-[var(--muted)]">Combat telemetry dashboard</p>
      <h1 className="mt-1 text-2xl font-bold tracking-tight text-[var(--ink)] sm:text-3xl">
        Performance overview
      </h1>
      <p className="mt-3 max-w-2xl text-base leading-relaxed text-[var(--muted)] sm:text-lg">
        You&apos;re at{" "}
        <span className="font-semibold text-[var(--accent)]">{winRate}%</span> win rate across{" "}
        <span className="font-semibold text-[var(--ink)]">{total}</span> recorded session
        {total === 1 ? "" : "s"}
        {sessionCount !== total && total > 0 ? (
          <>
            {" "}
            (<span className="tabular-nums">{sessionCount}</span> in current filter)
          </>
        ) : null}
        .
      </p>
      <div className="mt-6 flex flex-wrap items-center gap-6">
        <WinRing percent={winRate} />
        <div className="space-y-2 text-sm text-[var(--muted)]">
          <p>
            <span className="font-medium text-[var(--ink)]">{stats.bestScore ?? 0}</span> best score
          </p>
          <p>
            <span className="font-medium text-[var(--ink)]">{stats.totalHits ?? 0}</span> hits landed ·{" "}
            <span className="font-medium text-[var(--ink)]">{Math.round(stats.totalDamage ?? 0)}</span>{" "}
            damage
          </p>
          <p className="text-xs">Data source: db.json · realtime socket updates</p>
        </div>
      </div>
    </div>
  );
}

function WinRing({ percent }) {
  const clamped = Math.max(0, Math.min(100, Number(percent) || 0));
  const r = 36;
  const c = 2 * Math.PI * r;
  const offset = c - (clamped / 100) * c;
  return (
    <div className="relative h-24 w-24 shrink-0" aria-label={`Win rate ${clamped}%`}>
      <svg className="h-24 w-24 -rotate-90" viewBox="0 0 88 88">
        <circle cx="44" cy="44" r={r} fill="none" stroke="#e5e7eb" strokeWidth="8" />
        <circle
          cx="44"
          cy="44"
          r={r}
          fill="none"
          stroke="var(--accent)"
          strokeWidth="8"
          strokeLinecap="round"
          strokeDasharray={c}
          strokeDashoffset={offset}
        />
      </svg>
      <div className="absolute inset-0 flex flex-col items-center justify-center">
        <span className="text-lg font-bold tabular-nums text-[var(--ink)]">{clamped}%</span>
        <span className="text-[0.6rem] font-medium uppercase text-[var(--muted)]">Win</span>
      </div>
    </div>
  );
}

function HighlightCard({ label, value, hint, tone = "blue" }) {
  const styles = TONE_STYLES[tone] || TONE_STYLES.blue;
  return (
    <div className={`${card} p-5`}>
      <div className={`mb-3 inline-flex rounded-xl p-2 ${styles.icon}`}>
        <MetricIcon label={label} />
      </div>
      <p className="text-xs font-medium uppercase tracking-wide text-[var(--muted)]">{label}</p>
      <p className="mt-1 text-2xl font-bold tabular-nums text-[var(--ink)]">{value}</p>
      <p className="mt-2 text-xs leading-snug text-[var(--muted)]">{hint}</p>
    </div>
  );
}

function MetricIcon({ label }) {
  const size = "h-5 w-5";
  const l = label.toLowerCase();
  if (l.includes("session")) {
    return (
      <svg className={size} fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
        <path strokeLinecap="round" strokeLinejoin="round" d="M9 19v-6a2 2 0 00-2-2H5a2 2 0 00-2 2v6m16 0v-6a2 2 0 00-2-2h-2a2 2 0 00-2 2v6M9 9V5a2 2 0 012-2h2a2 2 0 012 2v4" />
      </svg>
    );
  }
  if (l.includes("score")) {
    return (
      <svg className={size} fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
        <path strokeLinecap="round" strokeLinejoin="round" d="M13 7h8m0 0v8m0-8l-8 8-4-4-6 6" />
      </svg>
    );
  }
  if (l.includes("win")) {
    return (
      <svg className={size} fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
        <path strokeLinecap="round" strokeLinejoin="round" d="M9 12l2 2 4-4m6 2a9 9 0 11-18 0 9 9 0 0118 0z" />
      </svg>
    );
  }
  return (
    <svg className={size} fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
      <path strokeLinecap="round" strokeLinejoin="round" d="M13 10V3L4 14h7v7l9-11h-7z" />
    </svg>
  );
}

function AnalyticsGrid({ stats }) {
  const items = [
    { label: "Hits landed", value: stats.totalHits ?? 0, sub: "Successful strikes", tone: "blue" },
    { label: "Total points", value: stats.totalScore ?? 0, sub: "Sum of session scores", tone: "indigo" },
    { label: "Victories", value: stats.victories ?? 0, sub: "Sessions won", tone: "green" },
    { label: "Defeats", value: stats.defeats ?? 0, sub: "Sessions lost", tone: "rose" },
    { label: "Taps", value: stats.totalTaps ?? 0, sub: "Input events tracked", tone: "amber" },
    { label: "Avg damage", value: avgDamage(stats), sub: "Per session", tone: "slate" },
  ];
  return (
    <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
      {items.map((item) => (
        <div key={item.label} className={`${card} flex gap-4 p-4`}>
          <div className={`flex h-10 w-10 shrink-0 items-center justify-center rounded-xl ${TONE_STYLES[item.tone].icon}`}>
            <MetricIcon label={item.label} />
          </div>
          <div className="min-w-0">
            <p className="text-xs font-medium uppercase tracking-wide text-[var(--muted)]">{item.label}</p>
            <p className="text-xl font-bold tabular-nums text-[var(--ink)]">{item.value}</p>
            <p className="text-xs text-[var(--muted)]">{item.sub}</p>
          </div>
        </div>
      ))}
    </div>
  );
}

function avgDamage(stats) {
  const n = Number(stats.totalSessions) || 0;
  if (!n) return 0;
  return Math.round((Number(stats.totalDamage) || 0) / n);
}

function ScoreTrendChart({ sessions }) {
  const [activeIndex, setActiveIndex] = useState(null);
  const chartId = useMemo(() => `score-chart-${Math.random().toString(36).slice(2, 9)}`, []);
  const width = 640;
  const height = 220;
  const pad = { top: 20, right: 16, bottom: 36, left: 44 };
  const innerW = width - pad.left - pad.right;
  const innerH = height - pad.top - pad.bottom;

  if (!sessions.length) {
    return (
      <div className="flex h-[220px] items-center justify-center rounded-xl border border-dashed border-[var(--border)] bg-[var(--surface-muted)] text-sm text-[var(--muted)]">
        <div className="text-center">
          <p className="font-medium text-[var(--ink)]">No session data yet</p>
          <p className="mt-1 text-xs">Complete a VR session to see your score graph.</p>
        </div>
      </div>
    );
  }

  const scores = sessions.map((x) => Number(x.score) || 0);
  const maxY = Math.max(1, ...scores);
  const minY = 0;
  const n = sessions.length;
  const latest = scores[n - 1];
  const peak = maxY;
  const prev = n > 1 ? scores[n - 2] : null;
  const delta = prev !== null ? latest - prev : null;

  const points = sessions.map((row, i) => {
    const x = pad.left + (n === 1 ? innerW / 2 : (i / (n - 1)) * innerW);
    const y = pad.top + innerH - ((scores[i] - minY) / (maxY - minY)) * innerH;
    const baseline = pad.top + innerH;
    return { x, y, baseline, row, score: scores[i], i };
  });

  const linePath =
    n === 1
      ? null
      : points.map((p, i) => `${i === 0 ? "M" : "L"} ${p.x.toFixed(1)} ${p.y.toFixed(1)}`).join(" ");

  const areaPath =
    linePath &&
    `${linePath} L ${points[n - 1].x.toFixed(1)} ${(pad.top + innerH).toFixed(1)} L ${points[0].x.toFixed(1)} ${(pad.top + innerH).toFixed(1)} Z`;

  const yTicks = [0, Math.round(maxY / 2), maxY];
  const hover = activeIndex !== null ? points[activeIndex] : null;

  return (
    <div className="space-y-4">
      <div className="flex flex-wrap gap-3">
        <ChartStat label="Latest score" value={latest} hint={sessions[n - 1]?.playerName} />
        <ChartStat label="Peak in view" value={peak} hint={`${n} session${n === 1 ? "" : "s"}`} />
        <ChartStat
          label="Vs previous"
          value={delta === null ? "—" : delta > 0 ? `+${delta}` : String(delta)}
          hint={delta === null ? "First point" : delta >= 0 ? "Improving" : "Lower than last"}
          tone={delta === null ? "neutral" : delta > 0 ? "up" : delta < 0 ? "down" : "neutral"}
        />
      </div>

      <div className="relative overflow-hidden rounded-xl border border-[var(--border)] bg-gradient-to-b from-[var(--accent-soft)]/40 to-[var(--surface)]">
        <svg
          viewBox={`0 0 ${width} ${height}`}
          className="chart-svg h-[220px] w-full min-w-[300px]"
          role="img"
          aria-label="Score trend over recent sessions"
          onMouseLeave={() => setActiveIndex(null)}
        >
          <defs>
            <linearGradient id={`${chartId}-area`} x1="0" x2="0" y1="0" y2="1">
              <stop offset="0%" stopColor="#2563eb" stopOpacity="0.28" />
              <stop offset="100%" stopColor="#2563eb" stopOpacity="0.02" />
            </linearGradient>
            <linearGradient id={`${chartId}-bar`} x1="0" x2="0" y1="0" y2="1">
              <stop offset="0%" stopColor="#93c5fd" stopOpacity="0.9" />
              <stop offset="100%" stopColor="#2563eb" stopOpacity="0.35" />
            </linearGradient>
          </defs>

          <text x={pad.left - 8} y={14} textAnchor="end" fill="var(--muted)" fontSize="9" fontWeight="500">
            Score
          </text>

          {yTicks.map((tick) => {
            const y = pad.top + innerH - ((tick - minY) / (maxY - minY)) * innerH;
            return (
              <g key={tick}>
                <line
                  x1={pad.left}
                  x2={width - pad.right}
                  y1={y}
                  y2={y}
                  stroke="#e5e7eb"
                  strokeWidth="1"
                  strokeDasharray={tick === 0 ? "0" : "4 4"}
                />
                <text x={pad.left - 8} y={y + 3} textAnchor="end" fill="var(--muted)" fontSize="10">
                  {tick}
                </text>
              </g>
            );
          })}

          {n > 1 &&
            points.map((p) => {
              const barW = Math.max(8, innerW / n - 6);
              const barH = p.baseline - p.y;
              return (
                <rect
                  key={`bar-${p.row.id}`}
                  x={p.x - barW / 2}
                  y={p.y}
                  width={barW}
                  height={Math.max(0, barH)}
                  rx={4}
                  fill={`url(#${chartId}-bar)`}
                  opacity={activeIndex === null || activeIndex === p.i ? 0.35 : 0.12}
                />
              );
            })}

          {areaPath && (
            <path d={areaPath} fill={`url(#${chartId}-area)`} className="chart-area" />
          )}

          {linePath && (
            <path
              d={linePath}
              fill="none"
              stroke="#2563eb"
              strokeWidth="2.5"
              strokeLinecap="round"
              strokeLinejoin="round"
              className="chart-line"
            />
          )}

          {n === 1 && (
            <line
              x1={pad.left}
              x2={width - pad.right}
              y1={points[0].y}
              y2={points[0].y}
              stroke="#2563eb"
              strokeWidth="1"
              strokeDasharray="6 4"
              opacity="0.45"
            />
          )}

          {hover && (
            <line
              x1={hover.x}
              x2={hover.x}
              y1={pad.top}
              y2={pad.top + innerH}
              stroke="#2563eb"
              strokeWidth="1"
              opacity="0.35"
            />
          )}

          {points.map((p) => (
            <g key={p.row.id}>
              <circle
                cx={p.x}
                cy={p.y}
                r={14}
                fill="transparent"
                onMouseEnter={() => setActiveIndex(p.i)}
                onFocus={() => setActiveIndex(p.i)}
                tabIndex={0}
                aria-label={`Session ${p.i + 1}, score ${p.score}`}
              />
              <circle
                cx={p.x}
                cy={p.y}
                r={activeIndex === p.i ? 6 : n === 1 ? 7 : 4.5}
                fill="#ffffff"
                stroke="#2563eb"
                strokeWidth={activeIndex === p.i ? 3 : 2}
                className="transition-all duration-200"
                pointerEvents="none"
              />
              <text
                x={p.x}
                y={height - 10}
                textAnchor="middle"
                fill={activeIndex === p.i ? "#2563eb" : "var(--muted)"}
                fontSize="9"
                fontWeight={activeIndex === p.i ? "600" : "400"}
              >
                {n <= 8 ? formatShortDate(p.row.endedAt || p.row.startedAt) : p.i % 2 === 0 ? `#${p.i + 1}` : ""}
              </text>
            </g>
          ))}
        </svg>

        {hover && (
          <div
            className="pointer-events-none absolute z-10 max-w-[200px] rounded-lg border border-[var(--border)] bg-[var(--surface)] px-3 py-2 text-xs shadow-md"
            style={{
              left: `${Math.min(92, Math.max(8, (hover.x / width) * 100))}%`,
              top: "12px",
              transform: "translateX(-50%)",
            }}
          >
            <p className="font-semibold text-[var(--ink)]">{hover.score} pts</p>
            <p className="mt-0.5 text-[var(--muted)]">{hover.row.playerName}</p>
            <p className="text-[var(--muted)]">{formatTime(hover.row.endedAt || hover.row.startedAt)}</p>
            <p className="mt-1 capitalize text-[var(--accent)]">{hover.row.outcome || "session"}</p>
          </div>
        )}
      </div>
    </div>
  );
}

function ChartStat({ label, value, hint, tone = "neutral" }) {
  const valueClass =
    tone === "up"
      ? "text-[var(--good)]"
      : tone === "down"
        ? "text-[var(--bad)]"
        : "text-[var(--ink)]";
  return (
    <div className="min-w-[120px] flex-1 rounded-xl border border-[var(--border)] bg-[var(--surface)] px-4 py-3 shadow-sm">
      <p className="text-[0.65rem] font-medium uppercase tracking-wide text-[var(--muted)]">{label}</p>
      <p className={`mt-1 text-xl font-bold tabular-nums ${valueClass}`}>{value}</p>
      <p className="mt-0.5 truncate text-xs text-[var(--muted)]">{hint}</p>
    </div>
  );
}

function WinRatePanel({ winRate, victories, defeats }) {
  const clamped = Math.max(0, Math.min(100, winRate));
  return (
    <div className="px-5 py-4">
      <p className="text-2xl font-bold tabular-nums tracking-tight text-[var(--ink)]">{winRate}%</p>
      <p className="mt-1 text-sm text-[var(--muted)]">
        {victories} {victories === 1 ? "victory" : "victories"} · {defeats}{" "}
        {defeats === 1 ? "defeat" : "defeats"}
      </p>
      <div className="mt-4 h-2 w-full overflow-hidden rounded-full bg-gray-100">
        <div
          className="h-full rounded-full bg-[var(--accent)] transition-[width] duration-300"
          style={{ width: `${clamped}%` }}
          role="progressbar"
          aria-valuenow={clamped}
          aria-valuemin={0}
          aria-valuemax={100}
          aria-label="Win rate"
        />
      </div>
    </div>
  );
}

function MomentumPanel({ rows }) {
  if (!rows.length) {
    return (
      <p className="px-5 py-6 text-sm text-[var(--muted)]">
        No recent activity. Completed sessions appear here with outcome and score.
      </p>
    );
  }

  return (
    <ul className="divide-y divide-[var(--border)]">
      {rows.map((row, i) => {
        const prev = rows[i + 1];
        const trend =
          prev && Number.isFinite(Number(row.score)) && Number.isFinite(Number(prev.score))
            ? Number(row.score) - Number(prev.score)
            : null;
        return (
          <li
            key={row.id}
            className="flex items-center gap-2 px-5 py-2.5 text-sm transition-colors hover:bg-[var(--surface-muted)]"
          >
            <div className="min-w-0 flex-1">
              <p className="truncate font-medium text-[var(--ink)]">{row.playerName}</p>
              <p className="text-[0.65rem] text-[var(--muted)]">
                {formatEventTime(row.endedAt || row.startedAt)} · {row.level || "—"}
              </p>
            </div>
            <span className="tabular-nums font-medium">{row.score}</span>
            <OutcomeLabel outcome={row.outcome} />
            {trend !== null && (
              <span
                className={`w-10 text-right text-xs tabular-nums ${
                  trend > 0 ? "text-[var(--good)]" : trend < 0 ? "text-[var(--bad)]" : "text-[var(--muted)]"
                }`}
              >
                {trend > 0 ? `+${trend}` : trend}
              </span>
            )}
          </li>
        );
      })}
    </ul>
  );
}

function OutcomeLabel({ outcome }) {
  const isWin = outcome === "victory";
  const isLoss = outcome === "defeat";
  return (
    <span
      className={`inline-flex rounded-full px-2 py-0.5 text-[0.65rem] font-semibold uppercase tracking-wide ${
        isWin
          ? "bg-[var(--good-soft)] text-[var(--good)]"
          : isLoss
            ? "bg-[var(--bad-soft)] text-[var(--bad)]"
            : "bg-gray-100 text-[var(--muted)]"
      }`}
    >
      {outcome || "—"}
    </span>
  );
}

function SessionsTable({ rows }) {
  return (
    <div className="scrollbar-thin overflow-x-auto">
      <table className="w-full min-w-[640px] border-collapse">
        <thead>
          <tr className="border-b border-[var(--border)] bg-[var(--surface-muted)]">
            <th className={thClass}>Time</th>
            <th className={thClass}>Player</th>
            <th className={thClass}>Level</th>
            <th className={thClass}>Result</th>
            <th className={`${thClass} text-right`}>Score</th>
            <th className={`${thClass} text-right`}>Hits</th>
            <th className={`${thClass} text-right`}>Damage</th>
            <th className={`${thClass} text-right`}>Taps</th>
          </tr>
        </thead>
        <tbody>
          {rows.length === 0 && (
            <tr>
              <td colSpan={8} className="px-3 py-8 text-center text-sm text-[var(--muted)]">
                <p className="font-medium uppercase tracking-wide text-xs">No session data</p>
                <p className="mt-1 text-xs">Play a round in Unity with the backend running.</p>
              </td>
            </tr>
          )}
          {rows.map((row) => (
            <tr
              key={row.id}
              className="border-b border-[var(--border)] transition-colors last:border-0 hover:bg-[var(--surface-muted)]"
            >
              <td className={`${tdClass} text-[var(--muted)]`}>
                {formatTime(row.endedAt || row.startedAt)}
              </td>
              <td className={tdClass}>{row.playerName}</td>
              <td className={`${tdClass} text-[var(--muted)]`}>{row.level}</td>
              <td className={tdClass}>
                <OutcomeLabel outcome={row.outcome} />
              </td>
              <td className={`${tdClass} text-right font-medium`}>{row.score}</td>
              <td className={`${tdClass} text-right`}>{row.hitCount ?? 0}</td>
              <td className={`${tdClass} text-right`}>{Math.round(row.damageDealt || 0)}</td>
              <td className={`${tdClass} text-right`}>{row.tapCount ?? 0}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

function eventAccent(type) {
  const t = String(type || "").toLowerCase();
  if (t.includes("session")) return "var(--good)";
  if (t.includes("hit")) return "var(--accent)";
  if (t.includes("defeat") || t.includes("fail")) return "var(--bad)";
  return "var(--muted)";
}

function EventFeed({ events, apiOnline }) {
  if (!apiOnline && events.length === 0) {
    return (
      <p className="px-5 py-8 text-sm text-[var(--muted)]">
        Backend disconnected. Events will resume when the telemetry API is reachable.
      </p>
    );
  }

  if (events.length === 0) {
    return (
      <p className="px-5 py-8 text-sm text-[var(--muted)]">
        Waiting for combat events (hits, session completion)…
      </p>
    );
  }

  return (
    <ul className="scrollbar-thin max-h-[320px] overflow-y-auto">
      {events.map((ev) => (
        <li
          key={ev.id}
          className="flex gap-2 border-b border-[var(--border)] px-5 py-3 text-sm last:border-0 hover:bg-[var(--surface-muted)]"
        >
          <span
            className="w-0.5 shrink-0 self-stretch rounded-sm opacity-80"
            style={{ background: eventAccent(ev.type) }}
            aria-hidden
          />
          <time className="w-[4.25rem] shrink-0 tabular-nums text-[0.65rem] text-[var(--muted)]">
            {formatEventTime(ev.at)}
          </time>
          <div className="min-w-0 flex-1 pl-1">
            <p className="font-mono text-[0.65rem] font-medium uppercase tracking-wide text-[var(--ink)]">
              {ev.type}
            </p>
            <p className="mt-0.5 text-xs leading-snug text-[var(--muted)]">{ev.message}</p>
          </div>
        </li>
      ))}
    </ul>
  );
}

function LeaderboardTable({ rows }) {
  return (
    <div className="scrollbar-thin overflow-x-auto">
      <table className="w-full min-w-[480px] border-collapse">
        <thead>
          <tr className="border-b border-[var(--border)] bg-[var(--surface-muted)]">
            <th className={`${thClass} w-12`}>Rank</th>
            <th className={thClass}>Player</th>
            <th className={`${thClass} text-right`}>Score</th>
            <th className={thClass}>Result</th>
            <th className={thClass}>Session</th>
          </tr>
        </thead>
        <tbody>
          {rows.length === 0 && (
            <tr>
              <td colSpan={5} className="px-3 py-6 text-center text-sm text-[var(--muted)]">
                No scores yet. Complete a session to populate the leaderboard.
              </td>
            </tr>
          )}
          {rows.map((row, i) => (
            <tr
              key={row.id}
              className="border-b border-[var(--border)] transition-colors last:border-0 hover:bg-[var(--surface-muted)]"
            >
              <td className={`${tdClass} text-[var(--muted)]`}>{i + 1}</td>
              <td className={tdClass}>{row.playerName}</td>
              <td className={`${tdClass} text-right font-medium`}>{row.score}</td>
              <td className={tdClass}>
                <OutcomeLabel outcome={row.outcome} />
              </td>
              <td className={`${tdClass} text-[var(--muted)]`}>
                {row.level ? `${row.level} · ` : ""}
                {formatTime(row.endedAt)}
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}
