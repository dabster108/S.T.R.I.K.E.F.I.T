// STRIKEFIT Wrapped, the pure parts (tested in wrapped.test.js): the week and day labels, the five slides' text, the story's controls and
// the link. The numbers are the API's; this file only words them. `copy` and `slideIds` are passed in so the file imports no config JSON.

import { COPY, SLIDE_IDS } from "../config/wrapped.js";

const MONTHS = ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];
const WEEKDAYS = ["Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday"];
const parts = (day) => {
  const [y, m, d] = String(day).split("-").map(Number);
  return { y, m, d, dow: new Date(Date.UTC(y, m - 1, d)).getUTCDay() };
};
const num = new Intl.NumberFormat("en-US");
const one = new Intl.NumberFormat("en-US", { maximumFractionDigits: 1 });
const two = new Intl.NumberFormat("en-US", { maximumFractionDigits: 2 });

/** "12 to 18 Oct 2026", or "28 Sep to 4 Oct 2026" across a month, or with both years across New Year. */
export function weekLabel(weekStart, weekEnd) {
  const a = parts(weekStart);
  const b = parts(weekEnd);
  if (a.y !== b.y) return `${a.d} ${MONTHS[a.m - 1]} ${a.y} to ${b.d} ${MONTHS[b.m - 1]} ${b.y}`;
  if (a.m !== b.m) return `${a.d} ${MONTHS[a.m - 1]} to ${b.d} ${MONTHS[b.m - 1]} ${b.y}`;
  return `${a.d} to ${b.d} ${MONTHS[b.m - 1]} ${b.y}`;
}

/** "Wednesday 15 Oct" for a YYYY-MM-DD day (a calendar day, no time zone involved). */
export function dayLabel(day) {
  const p = parts(day);
  return `${WEEKDAYS[p.dow]} ${p.d} ${MONTHS[p.m - 1]}`;
}

export const hasFights = (data) => Boolean(data && data.fights > 0);
const pct = (v) => `${Math.round(v * 100)}%`;
const fightsText = (n) => `${num.format(n)} fight${n === 1 ? "" : "s"}`;

/**
 * The five slides of a week as plain text: [{ id, kicker, value, unit, headline, lines, note, alt }]. `monster` is the favourite monster
 * as the page knows it ({ name, weapon }) from story.json, looked up by the caller from data.favouriteMonster.level.
 * `alt` is the whole slide in one sentence, for screen readers.
 */
export function buildSlides(data, { monster = null } = {}) {
  if (!hasFights(data)) return [];
  const winLine = data.decidedFights ? `${data.wins} won of ${data.decidedFights} decided (${one.format(data.winRate)}%)` : "No decided fights";
  const swings = {
    id: "swings",
    kicker: "Your swings",
    value: num.format(data.swings),
    unit: data.swings === 1 ? "swing counted" : "swings counted",
    headline: "That is what you threw this week.",
    lines: [fightsText(data.fights), winLine, `${num.format(data.xpGained)} XP gained`],
    note: data.longestWinStreak >= 2 ? `Longest win streak: ${data.longestWinStreak}` : null,
  };
  swings.alt = `Your swings: ${swings.value} counted in ${fightsText(data.fights)}. ${winLine}. ${swings.lines[2]}.`;

  const fastest = {
    id: "fastest",
    kicker: "Your fastest swing",
    value: data.fastestSwing === null ? "—" : two.format(data.fastestSwing),
    unit: data.fastestSwing === null ? "" : "m/s",
    headline: data.fastestSwing === null ? COPY.noSwingSpeed : data.fastestSwing >= data.speedCap ? "Right at the game's limit." : "Your hardest hit of the week.",
    lines: [
      data.fullPowerShare === null ? "Full-power share: not measured" : `${pct(data.fullPowerShare)} of your swings were full power`,
      data.averageFade ? `Effort fade ${data.averageFade.fadePct > 0 ? "+" : ""}${one.format(data.averageFade.fadePct)}%: ${data.averageFade.labelText.toLowerCase()}` : COPY.noFade,
    ],
    note: data.fastestSwing !== null ? `Speeds are clamped to the game's ${data.speedCap} m/s cap.` : null,
  };
  fastest.alt = `Your fastest swing: ${data.fastestSwing === null ? COPY.noSwingSpeed : `${fastest.value} metres per second`}. ${fastest.lines.join(". ")}.`;

  const fm = data.favouriteMonster;
  const monsterSlide = {
    id: "monster",
    kicker: "Your favourite monster",
    value: fm ? (monster?.name || `Level ${fm.levelNumber ?? "?"}`) : "—",
    unit: "",
    headline: fm ? `You fought it in ${fightsText(fm.fights)}.` : COPY.noMonster,
    lines: fm ? [`${num.format(Math.round(fm.damageDealt))} damage dealt`, monster?.weapon ? `Weapon: ${monster.weapon}` : null].filter(Boolean) : [],
    note: null,
  };
  monsterSlide.alt = fm ? `Your favourite monster: ${monsterSlide.value}, fought ${fightsText(fm.fights)}, ${num.format(Math.round(fm.damageDealt))} damage dealt.` : COPY.noMonster;

  const bd = data.bestDay;
  const bestDay = {
    id: "bestDay",
    kicker: "Your best day",
    value: bd ? dayLabel(bd.day) : "—",
    unit: "",
    headline: bd ? `${num.format(bd.swings)} swings in one day.` : COPY.noBestDay,
    lines: [`${num.format(data.activeMinutes)} active minute${data.activeMinutes === 1 ? "" : "s"} in the week`, data.longestWinStreak ? `Longest win streak: ${data.longestWinStreak}` : "No win streak yet"],
    note: null,
  };
  bestDay.alt = bd ? `Your best day: ${bestDay.value}, with ${num.format(bd.swings)} counted swings. ${bestDay.lines.join(". ")}.` : COPY.noBestDay;

  const p = data.personality;
  const waiting = p.status !== "ok";
  const personality = {
    id: "personality",
    kicker: "Your effort personality",
    value: waiting ? COPY.personalityWaiting : p.name,
    unit: "",
    headline: waiting ? COPY.personalityWaitingText : p.reason,
    lines: [],
    note: null,
    axes: (p.axes || []).filter((a) => typeof a.score === "number"),
    waiting,
  };
  personality.alt = waiting ? `Your effort personality: ${COPY.personalityWaiting}. ${COPY.personalityWaitingText}` : `Your effort personality: ${p.name}. ${p.reason} ${personality.axes.map((a) => `${a.label} ${a.score}`).join(", ")}.`;

  const byId = { swings, fastest, monster: monsterSlide, bestDay, personality };
  return SLIDE_IDS.map((id) => byId[id]);
}

// ---------------------------------------------------------------------------------------------------------------- the story's controls

/** The slide after moving `dir` (-1 or 1) from `index`, kept inside 0..count-1 (no wrap-around). */
export const stepIndex = (index, dir, count) => Math.max(0, Math.min(count - 1, index + dir));

/** What a key does in the story: "next", "prev" or null. */
export function keyAction(key) {
  if (key === "ArrowRight" || key === "ArrowDown" || key === "PageDown") return "next";
  if (key === "ArrowLeft" || key === "ArrowUp" || key === "PageUp") return "prev";
  return null;
}

// ---------------------------------------------------------------------------------------------------------------- links and files

/** /wrapped/Ava or /wrapped/Ava?week=2026-10-12 (names are encoded). */
export const wrappedPath = (player, weekStart = null) => `/wrapped/${encodeURIComponent(player)}${weekStart ? `?week=${weekStart}` : ""}`;

/** The week from ?week=, or null (the API then uses the current week). Anything that is not a date is ignored. */
export const weekParam = (search) => {
  const v = new URLSearchParams(search).get("week");
  return v && /^\d{4}-\d{2}-\d{2}$/.test(v) ? v : null;
};

export const exportFileName = (player, weekStart, slideId) => `strikefit-wrapped-${String(player).replace(/[^a-z0-9]+/gi, "_")}-${weekStart}-${slideId}.png`;
