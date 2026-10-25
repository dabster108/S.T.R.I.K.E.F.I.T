import { useEffect, useRef, useState } from "react";
import { useReducedMotion } from "../../hooks/motion.js";
import { autoplayAllowed, claim, release, saveDataOn, shouldPlay } from "../../lib/videoCoordinator.js";
import Icon from "./Icon.jsx";

const BASE = import.meta.env.BASE_URL;
const urlOf = (path) => (path ? BASE + path.replace(/^\//, "") : null);

// path -> "ok" | "missing", shared by every DemoVideo on the page (one HEAD request per file). A missing file on this site answers
// 200 with the HTML shell, so only a video/* content type counts.
const checks = new Map();
function checkClip(src) {
  if (!checks.has(src)) {
    checks.set(
      src,
      fetch(src, { method: "HEAD" })
        .then((r) => (r.ok && /^video\//.test(r.headers.get("content-type") || "") ? "ok" : "missing"))
        .catch(() => "missing")
    );
  }
  return checks.get(src);
}

/**
 * A muted demo clip that fills its (fixed-ratio) parent and, like a product page's hero clip, starts by itself and loops for as long
 * as the page is open.
 *   - downloads with the page (preload="auto") and starts as soon as it can (native autoplay); scrolling never stops it;
 *   - every active clip on the page plays; one the visitor starts with Play pauses the one they started before
 *     (lib/videoCoordinator.js); the "Expand" copy takes over while it is open (the page copy is not `active`);
 *   - no autoplay under reduced motion, Calm mode or Data Saver: the poster shows with a Play button and nothing preloads;
 *   - a play / pause button (keyboard: Tab, then Enter or Space); a pause lasts until Play is pressed; out of the tab order while
 *     not `active`;
 *   - the clip shows at once while the file is checked; if it is missing or fails to load, `fallback` shows in the same box.
 * mp4 / webm / poster are paths under public/. `label` is the clip's text alternative, `credit` a short line shown on the clip,
 * onStatus("checking" | "ok" | "missing") lets the parent show things that only make sense with a real clip.
 */
export default function DemoVideo({ mp4, webm, poster, label, credit, active = true, fallback = null, className = "", onStatus, onVideoRef }) {
  const reduced = useReducedMotion();
  const [saveData] = useState(() => saveDataOn());
  const [status, setStatus] = useState("checking");
  const [choice, setChoice] = useState(null);
  const [playing, setPlaying] = useState(false);
  const ref = useRef(null);
  const src = urlOf(mp4);

  useEffect(() => {
    if (onStatus) onStatus(status);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [status]);

  useEffect(() => {
    let alive = true;
    if (!src) setStatus("missing");
    else checkClip(src).then((s) => alive && setStatus(s));
    return () => {
      alive = false;
    };
  }, [src]);

  // the visitor's choice lasts until the clip stops being the active one (the larger copy opened over it)
  useEffect(() => {
    if (!active) setChoice(null);
  }, [active]);

  useEffect(() => {
    const v = ref.current;
    if (!v || status === "missing") return;
    v.muted = true; // React does not always set the attribute; autoplay needs the property
    if (shouldPlay({ active, choice, reduced, saveData })) {
      if (choice === "play") claim(v);
      v.play().catch(() => {});
    } else if (!v.paused) v.pause();
  }, [status, active, choice, reduced, saveData]);

  useEffect(() => {
    const v = ref.current;
    return () => v && release(v);
  }, [status]);

  useEffect(() => {
    if (onVideoRef) onVideoRef(status !== "missing" ? ref.current : null);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [status]);

  const toggle = () => setChoice(playing ? "pause" : "play");
  // Only the video's own error (a decode failure) or the last source's (every source failed). React hands a <source>'s error to
  // the <video>'s onError as well, and the WebM source failing is normal where WebM is not served or not supported.
  const failed = (e) => e.target === e.currentTarget && setStatus("missing");
  const auto = active && autoplayAllowed({ reduced, saveData });

  return (
    <div className={`absolute inset-0 ${className}`}>
      {status === "missing" ? (
        fallback
      ) : (
        <>
          <video
            ref={ref}
            muted
            loop
            playsInline
            autoPlay={auto}
            preload={auto ? "auto" : "metadata"} // with autoplay off only the first frame's data is fetched until Play
            poster={urlOf(poster) || undefined}
            aria-label={label}
            onPlay={() => setPlaying(true)}
            onPause={() => setPlaying(false)}
            onError={failed}
            className="h-full w-full bg-black object-cover"
          >
            {webm && <source src={urlOf(webm)} type="video/webm" />}
            <source src={src} type="video/mp4" onError={failed} />
          </video>

          {/* no autoplay (reduced motion, Calm mode, Data Saver) and not started: a clear Play button over the poster */}
          {!playing && choice !== "pause" && (reduced || saveData) && (
            <button
              type="button"
              onClick={toggle}
              tabIndex={active ? undefined : -1}
              aria-label={`Play the clip: ${label}`}
              className="press absolute inset-0 m-auto flex h-14 w-14 items-center justify-center rounded-full bg-black/60 text-white shadow-lg ring-2 ring-white/70 backdrop-blur hover:bg-black/75 sm:h-16 sm:w-16"
            >
              <Icon name="play" className="ml-0.5 h-6 w-6 sm:h-7 sm:w-7" strokeWidth={2.4} />
            </button>
          )}

          <div className="pointer-events-none absolute inset-x-0 bottom-0 flex items-end justify-between gap-3 bg-gradient-to-t from-black/75 via-black/35 to-transparent px-3 pb-2.5 pt-8 sm:px-4 sm:pb-3">
            {credit ? <p className="max-w-[80%] text-[0.7rem] font-semibold leading-snug text-white sm:text-xs">{credit}</p> : <span />}
            <button
              type="button"
              onClick={toggle}
              tabIndex={active ? undefined : -1}
              aria-label={playing ? "Pause the clip" : "Play the clip"}
              className="press pointer-events-auto flex h-11 w-11 shrink-0 items-center justify-center rounded-full bg-black/55 text-white ring-1 ring-white/50 backdrop-blur hover:bg-black/75"
            >
              <Icon name={playing ? "pause" : "play"} className="h-4 w-4" strokeWidth={2.4} />
            </button>
          </div>
        </>
      )}
    </div>
  );
}
