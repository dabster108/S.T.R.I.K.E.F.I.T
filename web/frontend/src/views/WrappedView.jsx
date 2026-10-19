import { useEffect, useRef, useState } from "react";
import { Link, useParams, useSearchParams } from "react-router-dom";
import { Brand } from "../components/landing/LandingNav.jsx";
import Story from "../components/wrapped/Story.jsx";
import { downloadBlob, renderCardPng } from "../components/wrapped/exportPng.js";
import { COPY, SLIDE_IDS } from "../config/wrapped.js";
import { useApi } from "../hooks/useApi.js";
import { useRank } from "../hooks/useRank.js";
import { api } from "../lib/api.js";
import { guardianForKey } from "../lib/story.js";
import { copyText, toast } from "../lib/toast.js";
import { buildSlides, exportFileName, hasFights, weekLabel, weekParam, wrappedPath } from "../lib/wrapped.js";
import Button, { buttonClass } from "../components/ui/Button.jsx";
import { EmptyState, Skeleton } from "../components/ui/Feedback.jsx";
import Icon from "../components/ui/Icon.jsx";

// /wrapped/:player?week=YYYY-MM-DD: a player's week as a five-slide story (swings, fastest swing, favourite monster, best day, effort
// personality), with a week picker, a PNG of the current slide and a link. Public like /story: the card shows the pseudonym and numbers only.

function Header({ name }) {
  return (
    <header className="border-b border-[var(--border)] bg-[var(--surface)]">
      <div className="mx-auto flex max-w-3xl items-center justify-between gap-3 px-4 py-3 sm:px-6">
        <Link to="/" aria-label="STRIKEFIT home">
          <Brand />
        </Link>
        {name && (
          <Link to={`/dashboard/player/${encodeURIComponent(name)}`} className={buttonClass("secondary", "sm")}>
            <Icon name="player" className="h-3.5 w-3.5" /> {name}'s profile
          </Link>
        )}
      </div>
    </header>
  );
}

function WeekPicker({ data, week, onWeek }) {
  const options = [...(data.weeks || [])];
  if (!options.some((w) => w.weekStart === data.weekStart)) options.unshift({ weekStart: data.weekStart, weekEnd: data.weekEnd, fights: data.fights });
  return (
    <div className="flex flex-wrap items-center justify-center gap-2" role="group" aria-label="Choose a week">
      <Button icon="chevronLeft" onClick={() => onWeek(data.previousWeek)} aria-label="Previous week" title="Previous week">
        <span className="sr-only">Previous week</span>
      </Button>
      <label className="sr-only" htmlFor="wrapped-week">
        Week
      </label>
      <select
        id="wrapped-week"
        value={data.weekStart}
        onChange={(e) => onWeek(e.target.value)}
        className="press rounded-lg border border-[var(--border)] bg-[var(--surface)] px-3 py-2 text-sm font-semibold text-[var(--ink)] shadow-sm"
      >
        {options.map((w) => (
          <option key={w.weekStart} value={w.weekStart}>
            {weekLabel(w.weekStart, w.weekEnd)} · {w.fights} fight{w.fights === 1 ? "" : "s"}
          </option>
        ))}
      </select>
      <Button onClick={() => onWeek(data.nextWeek)} disabled={!data.nextWeek} aria-label="Next week" title="Next week">
        <Icon name="chevronRight" className="h-3.5 w-3.5" />
        <span className="sr-only">Next week</span>
      </Button>
      {!data.isCurrentWeek && (
        <Button onClick={() => onWeek(null)}>This week</Button>
      )}
    </div>
  );
}

export default function WrappedView() {
  const { player: raw } = useParams();
  const name = decodeURIComponent(raw || "");
  const [params, setParams] = useSearchParams();
  const week = weekParam(`?${params.toString()}`);
  const { data: fetched, error, loading, reload } = useApi(() => api.playerWrapped(name, week), [name, week]);
  const data = fetched && fetched.playerName === name ? fetched : undefined;
  const rank = useRank(name);
  const [index, setIndex] = useState(0);
  const [busy, setBusy] = useState(false);
  const exportNode = useRef(null);

  useEffect(() => {
    document.title = `${name} · Wrapped · STRIKEFIT`;
  }, [name]);
  useEffect(() => setIndex(0), [name, week]);

  const setWeek = (w) => setParams(w ? { week: w } : {}, { replace: false });
  const slides = data ? buildSlides(data, { monster: data.favouriteMonster ? guardianForKey(data.favouriteMonster.level) : null }) : [];
  const slide = slides[index];

  const exportPng = async () => {
    if (!exportNode.current || busy || !slide) return;
    setBusy(true);
    try {
      const blob = await renderCardPng(exportNode.current);
      const file = exportFileName(name, data.weekStart, SLIDE_IDS[index]);
      downloadBlob(blob, file);
      toast.good("Image saved", file);
    } catch {
      toast.bad("Couldn't create the image", "Try again in a moment.");
    } finally {
      setBusy(false);
    }
  };
  const copyLink = () => copyText(`${window.location.origin}${wrappedPath(name, data?.weekStart)}`, "Link");

  let body;
  if (error?.status === 404 && !data) {
    body = <EmptyState icon="players" title="No fights for this player yet" description="Wrapped is made from finished fights. Names must match the name used on the headset." />;
  } else if (!data && !error) {
    body = (
      <div className="mx-auto w-full max-w-[34rem]" aria-busy="true" aria-label="Loading Wrapped">
        <Skeleton className="mb-3 h-1.5 w-full" />
        <Skeleton className="w-full rounded-2xl" style={{ aspectRatio: "4 / 5" }} />
      </div>
    );
  } else if (error && !data) {
    body = (
      <EmptyState
        icon="offline"
        title="Wrapped couldn't load"
        description="The server did not answer. Check the connection and try again."
        action={
          <Button onClick={reload} icon="refresh">
            Try again
          </Button>
        }
      />
    );
  } else if (!hasFights(data)) {
    const latest = (data.weeks || [])[0];
    body = (
      <>
        <WeekPicker data={data} week={week} onWeek={setWeek} />
        <div className="mt-6">
          <EmptyState
            icon="calendar"
            title={COPY.emptyTitle}
            description={COPY.emptyText}
            action={
              latest && (
                <Button variant="primary" onClick={() => setWeek(latest.weekStart)}>
                  See {weekLabel(latest.weekStart, latest.weekEnd)}
                </Button>
              )
            }
          />
        </div>
      </>
    );
  } else {
    body = (
      <>
        <WeekPicker data={data} week={week} onWeek={setWeek} />
        <div className="mt-5" style={{ opacity: loading ? 0.6 : 1 }}>
          <Story slides={slides} data={data} monsterLevel={data.favouriteMonster?.level} rank={rank.data} index={index} onIndex={setIndex} exportNodeRef={exportNode} />
        </div>
        <div className="mt-4 flex flex-wrap items-center justify-center gap-2">
          <Button variant="primary" icon="download" onClick={exportPng} disabled={busy}>
            {busy ? "Creating image…" : "Save this slide as PNG"}
          </Button>
          <Button icon="link" onClick={copyLink}>
            Copy link
          </Button>
        </div>
        <p className="mx-auto mt-4 max-w-md text-center text-xs font-medium text-[var(--muted)]">
          Week by the day each fight started (Asia/Kathmandu). Wins follow the site's win rate rule: abandoned fights are not counted as decided. Swing speeds are capped at {data.speedCap} m/s.
        </p>
      </>
    );
  }

  return (
    <div className="min-h-screen bg-[var(--bg)] text-[var(--ink)]">
      <Header name={name} />
      <main className="mx-auto max-w-3xl px-4 py-6 sm:px-6 sm:py-8">
        <h1 className="mb-5 text-center text-[length:var(--fs-h1)] font-extrabold tracking-tight">
          STRIKEFIT Wrapped<span className="sr-only">: {name}</span>
        </h1>
        {body}
      </main>
    </div>
  );
}
