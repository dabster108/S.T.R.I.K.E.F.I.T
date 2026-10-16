import { useEffect, useRef, useState } from "react";
import Button from "../ui/Button.jsx";

// "Challenge a friend": shares /challenge/<sessionId>. The link carries only the session id; the page reads the score
// from the API, so the target can't be edited in the URL. Web Share where the browser has it, else copy the link,
// else show it to copy by hand.
export const challengeUrl = (sessionId) => `${typeof window === "undefined" ? "" : window.location.origin}/challenge/${encodeURIComponent(sessionId)}`;

export default function ShareChallenge({ sessionId, playerName, level, size = "sm", variant = "secondary" }) {
  const [state, setState] = useState(null); // null | "copied" | "manual"
  const timer = useRef(null);
  useEffect(() => () => clearTimeout(timer.current), []);
  const url = challengeUrl(sessionId);
  const share = async () => {
    const text = `Can you beat ${playerName}'s score on ${level}? STRIKEFIT`;
    try {
      if (navigator.share) {
        await navigator.share({ title: "STRIKEFIT challenge", text, url });
        return;
      }
      await navigator.clipboard.writeText(url);
      setState("copied");
      clearTimeout(timer.current);
      timer.current = setTimeout(() => setState(null), 2500);
    } catch (err) {
      if (err?.name !== "AbortError") setState("manual");
    }
  };
  return (
    <span className="inline-flex flex-wrap items-center gap-2">
      <Button variant={variant} size={size} icon="share" onClick={share}>
        Challenge a friend
      </Button>
      <span role="status" aria-live="polite" className="text-xs font-semibold text-[var(--good-ink)]">
        {state === "copied" ? "Link copied" : ""}
      </span>
      {state === "manual" && (
        <input readOnly value={url} aria-label="Challenge link" onFocus={(e) => e.currentTarget.select()} className="w-64 rounded-lg border border-[var(--border)] bg-[var(--surface)] px-2 py-1 text-xs font-semibold text-[var(--ink)]" />
      )}
    </span>
  );
}
