import { useEffect, useRef } from "react";
import { useReducedMotion } from "../../hooks/motion.js";

// Types `text` out one character at a time each time `active` turns true (any other time it shows the whole line). The real text stays in the DOM (transparent, so it
// holds the line's final size and screen readers read it); the typed copy is an aria-hidden overlay written straight to the DOM,
// so typing never re-renders React. Reduced motion and Calm mode show the finished line.
export default function TypedLine({ text, active, className = "", msPerChar = 22 }) {
  const out = useRef(null);
  const reduced = useReducedMotion();
  useEffect(() => {
    const el = out.current;
    if (!el) return undefined;
    if (reduced || !active) {
      el.textContent = text; // a line that is not being typed is shown whole
      return undefined;
    }
    let i = 0;
    el.textContent = "";
    const t = setInterval(() => {
      i += 1;
      el.textContent = text.slice(0, i);
      if (i >= text.length) clearInterval(t);
    }, msPerChar);
    return () => clearInterval(t);
  }, [text, active, reduced, msPerChar]);
  return (
    <span className={`grid ${className}`}>
      <span className="col-start-1 row-start-1 opacity-0">{text}</span>
      <span ref={out} className="col-start-1 row-start-1" aria-hidden />
    </span>
  );
}
