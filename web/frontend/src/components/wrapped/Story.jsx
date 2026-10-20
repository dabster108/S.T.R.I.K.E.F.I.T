import { useCallback, useEffect, useRef, useState } from "react";
import { AUTO_ADVANCE_MS, CARD } from "../../config/wrapped.js";
import { useElementWidth } from "../charts/useElementWidth.js";
import { keyAction, stepIndex } from "../../lib/wrapped.js";
import Button from "../ui/Button.jsx";
import Icon from "../ui/Icon.jsx";
import SlideCard from "./SlideCard.jsx";

// The Wrapped story: five slides, progress bars on top, a tap on the card (left third back, the rest forward), the arrow keys, and
// auto-advance that is OFF until the viewer turns it on. Only transform and opacity ever animate. Each slide is announced to screen readers.

function isTyping(el) {
  return el && (el.tagName === "INPUT" || el.tagName === "SELECT" || el.tagName === "TEXTAREA" || el.isContentEditable);
}

export default function Story({ slides, data, monsterLevel, rank, index, onIndex, exportNodeRef }) {
  const [auto, setAuto] = useState(false);
  const [boxRef, width] = useElementWidth(CARD.w);
  const scale = Math.min(1, width / CARD.w) || 1;
  const count = slides.length;
  const go = useCallback((dir) => onIndex((i) => stepIndex(i, dir, count)), [onIndex, count]);
  const holder = useRef(null);

  // arrow keys, anywhere on the page except while typing in a field
  useEffect(() => {
    const onKey = (e) => {
      if (e.metaKey || e.ctrlKey || e.altKey || isTyping(e.target)) return;
      const act = keyAction(e.key);
      if (!act) return;
      e.preventDefault();
      go(act === "next" ? 1 : -1);
    };
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, [go]);

  // auto-advance (off by default): one step per AUTO_ADVANCE_MS, stops on the last slide, waits while the tab is hidden
  useEffect(() => {
    if (!auto) return undefined;
    if (index >= count - 1) return undefined;
    const t = setTimeout(() => {
      if (!document.hidden) go(1);
    }, AUTO_ADVANCE_MS);
    return () => clearTimeout(t);
  }, [auto, index, count, go]);
  useEffect(() => {
    if (index >= count - 1) setAuto(false);
  }, [index, count]);

  const slide = slides[index];
  return (
    <div ref={holder} className="mx-auto w-full max-w-[34rem]">
      {/* progress: one bar per slide; with auto-play on, the current bar fills over the slide's time (transform only) */}
      <ol className="mb-3 flex gap-1.5" aria-label={`Slide ${index + 1} of ${count}`}>
        {slides.map((s, i) => (
          <li key={s.id} className="h-1.5 flex-1 overflow-hidden rounded-full bg-[var(--border)]">
            <button type="button" onClick={() => onIndex(i)} className="block h-full w-full" aria-label={`Go to slide ${i + 1}: ${s.kicker}`} aria-current={i === index ? "step" : undefined}>
              <span
                key={i === index ? `${index}-${auto}` : i}
                className={`block h-full w-full origin-left rounded-full bg-[var(--accent)] ${i === index && auto ? "wr-progress" : ""}`}
                style={{ transform: i <= index ? "scaleX(1)" : "scaleX(0)", "--wr-ms": `${AUTO_ADVANCE_MS}ms` }}
              />
            </button>
          </li>
        ))}
      </ol>

      <div ref={boxRef} className="relative overflow-hidden rounded-2xl border border-[var(--border)] shadow-[var(--shadow-lift)]" style={{ height: CARD.h * scale }} role="group" aria-roledescription="slide" aria-label={`${index + 1} of ${count}: ${slide.kicker}`} data-testid="wrapped-card">
        <div key={slide.id} style={{ width: CARD.w, height: CARD.h, transform: `scale(${scale})`, transformOrigin: "top left" }}>
          <SlideCard slide={slide} data={data} monsterLevel={monsterLevel} rank={rank} animate />
        </div>
        {/* the tap zones sit over the card: the left third goes back, the rest goes forward */}
        <button type="button" className="absolute inset-y-0 left-0 w-1/3 cursor-w-resize focus-visible:outline-offset-[-4px]" aria-label="Previous slide" disabled={index === 0} onClick={() => go(-1)} />
        <button type="button" className="absolute inset-y-0 right-0 w-2/3 cursor-e-resize focus-visible:outline-offset-[-4px]" aria-label="Next slide" disabled={index === count - 1} onClick={() => go(1)} />
      </div>

      <p className="sr-only" aria-live="polite" data-testid="wrapped-live">{slide.alt}</p>

      <div className="mt-3 flex flex-wrap items-center justify-between gap-2">
        <div className="flex items-center gap-2">
          <Button icon="chevronLeft" onClick={() => go(-1)} disabled={index === 0} aria-label="Previous slide">Back</Button>
          <Button onClick={() => go(1)} disabled={index === count - 1} aria-label="Next slide">
            Next <Icon name="chevronRight" className="h-3.5 w-3.5" />
          </Button>
        </div>
        <Button icon={auto ? "pause" : "play"} onClick={() => setAuto((a) => !a)} aria-pressed={auto} disabled={index >= count - 1} title="Move to the next slide on its own">
          Auto-play: {auto ? "on" : "off"}
        </Button>
      </div>

      {/* the card the PNG is made from: the same slide at full size, off screen, with no entrance animation */}
      <div aria-hidden style={{ position: "fixed", left: -20000, top: 0, width: CARD.w, height: CARD.h, pointerEvents: "none" }}>
        <SlideCard slide={slide} data={data} monsterLevel={monsterLevel} rank={rank} animate={false} nodeRef={exportNodeRef} />
      </div>
    </div>
  );
}
