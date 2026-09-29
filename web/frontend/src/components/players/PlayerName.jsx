import { useEffect, useId, useRef, useState } from "react";
import { api } from "../../lib/api.js";
import { announceRename } from "../../live/liveStore.js";
import { buttonClass } from "../ui/Button.jsx";
import Icon from "../ui/Icon.jsx";

export const MAX_NAME = 40;

// Same field look as Controls.jsx, without its fixed text size.
const field =
  "min-w-0 rounded-lg border bg-[var(--surface)] px-2.5 text-[var(--ink)] shadow-sm transition-colors focus:outline-none focus:ring-2 disabled:opacity-60";

export function nameProblem(value) {
  const v = value.trim();
  if (!v) return "Enter a name.";
  if (v.length > MAX_NAME) return `Keep it to ${MAX_NAME} characters or fewer.`;
  return null;
}

// A player's name with a pencil that turns it into an inline field. On save it
// PATCHes the backend, hands the new profile to onRenamed, and announces the
// rename to the rest of the app (lists, live arena, leaderboard, toast).
// `children` is how the name looks when not editing (a link, a heading…).
export default function PlayerName({ name, size = "card", onRenamed, children }) {
  const [editing, setEditing] = useState(false);
  const [value, setValue] = useState(name);
  const [error, setError] = useState(null);
  const [saving, setSaving] = useState(false);
  const input = useRef(null);
  const trigger = useRef(null);
  const errorId = useId();
  const title = size === "title";

  useEffect(() => {
    if (editing) {
      input.current?.focus();
      input.current?.select();
    }
  }, [editing]);

  const open = (e) => {
    e.preventDefault();
    e.stopPropagation();
    setValue(name);
    setError(null);
    setEditing(true);
  };
  const close = () => {
    setEditing(false);
    setError(null);
    requestAnimationFrame(() => trigger.current?.focus());
  };

  const save = async (e) => {
    e.preventDefault();
    const next = value.trim();
    const problem = nameProblem(value);
    if (problem) {
      setError(problem);
      input.current?.focus();
      return;
    }
    if (next === name) return close();
    setSaving(true);
    setError(null);
    try {
      const profile = await api.renamePlayer(name, next);
      setEditing(false);
      onRenamed?.(profile, next);
      announceRename({ oldName: name, newName: next });
    } catch (err) {
      setError(err.status >= 400 && err.status < 500 ? err.message : `The rename didn't go through (${err.message}). Try again.`);
      requestAnimationFrame(() => input.current?.focus());
    } finally {
      setSaving(false);
    }
  };

  if (!editing) {
    return (
      <span className="group/name inline-flex min-w-0 max-w-full items-center gap-1">
        {children ?? <span className="truncate">{name}</span>}
        <button
          ref={trigger}
          type="button"
          onClick={open}
          aria-label={`Rename ${name}`}
          title="Rename player"
          className={`press relative z-[1] inline-flex shrink-0 items-center justify-center rounded-md text-[var(--muted)] opacity-0 transition-opacity hover:bg-[var(--surface-muted)] hover:text-[var(--accent)] focus-visible:opacity-100 group-hover/name:opacity-100 group-hover/card:opacity-100 [@media(hover:none)]:opacity-100 ${
            title ? "h-8 w-8" : "h-7 w-7"
          }`}
        >
          <Icon name="pencil" className={title ? "h-4 w-4" : "h-3.5 w-3.5"} />
        </button>
      </span>
    );
  }

  const count = value.trim().length;
  return (
    <form
      onSubmit={save}
      onKeyDown={(e) => {
        if (e.key === "Escape" && !saving) {
          e.stopPropagation();
          close();
        }
      }}
      className="relative z-[1] flex min-w-0 flex-col gap-1.5"
      aria-label={`Rename ${name}`}
    >
      <div className="flex min-w-0 flex-wrap items-center gap-2">
        <label className={`min-w-0 ${title ? "flex-1 basis-40" : "basis-full"}`}>
          <span className="sr-only">New name for {name}</span>
          <input
            ref={input}
            value={value}
            onChange={(e) => {
              setValue(e.target.value);
              if (error) setError(null);
            }}
            disabled={saving}
            autoComplete="off"
            spellCheck={false}
            aria-invalid={Boolean(error)}
            aria-describedby={error ? errorId : undefined}
            className={`${field} w-full ${title ? "py-1.5 text-xl font-extrabold tracking-tight" : "py-1 text-sm font-bold"} ${
              error ? "border-[var(--bad)] focus:border-[var(--bad)] focus:ring-[var(--bad-soft)]" : "border-[var(--border)] hover:border-[var(--border-strong)] focus:border-[var(--accent)] focus:ring-[var(--accent-ring)]"
            }`}
          />
        </label>
        <button type="submit" disabled={saving} className={buttonClass("primary", "sm")}>
          <Icon name={saving ? "loader" : "check"} className={`h-3.5 w-3.5 ${saving ? "animate-spin" : ""}`} />
          {saving ? "Saving…" : "Save"}
        </button>
        <button type="button" disabled={saving} onClick={close} className={buttonClass("ghost", "sm")}>
          Cancel
        </button>
      </div>
      <div className="flex min-h-4 items-start justify-between gap-3 text-xs">
        {error ? (
          <p id={errorId} role="alert" className="fade-in flex items-start gap-1.5 font-semibold text-[var(--bad)]">
            <Icon name="info" className="mt-px h-3.5 w-3.5 shrink-0" />
            {error}
          </p>
        ) : (
          <p className="font-medium text-[var(--muted)]">Enter to save · Esc to cancel</p>
        )}
        <span className={`shrink-0 font-semibold tabular-nums ${count > MAX_NAME ? "text-[var(--bad)]" : "text-[var(--muted)]"}`}>
          {count}/{MAX_NAME}
        </span>
      </div>
    </form>
  );
}
