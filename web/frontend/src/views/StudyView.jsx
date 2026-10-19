import { useCallback, useEffect, useState } from "react";
import { Link } from "react-router-dom";
import { api } from "../lib/api.js";
import { postBody, postMissing, preMissing, stepFor } from "../lib/study.js";
import { copyText, toast } from "../lib/toast.js";
import * as C from "../content/studyContent.js";
import { Brand } from "../components/landing/LandingNav.jsx";
import Button from "../components/ui/Button.jsx";
import { ToastStack, useToasts } from "../components/ui/Toasts.jsx";
import Icon from "../components/ui/Icon.jsx";

// /study: the public playtest study, no login. Consent -> a participant code (P01..P20, typed into the headset as the player
// name) -> a short form about you -> play -> the after-play questionnaire (Borg RPE, enjoyment, effort against a usual workout,
// "play again", the ten SUS items). The code lives in this browser's localStorage so a refresh or a return visit carries on.
// The server decides what step is next (lib/study.js stepFor), so a stale tab cannot skip ahead.

const KEY = "strikefit.study.code";
const store = {
  get: () => {
    try {
      return localStorage.getItem(KEY);
    } catch {
      return null;
    }
  },
  set: (v) => {
    try {
      v ? localStorage.setItem(KEY, v) : localStorage.removeItem(KEY);
    } catch {
      /* private mode: the code is shown on screen to write down instead */
    }
  },
};

const field = "min-h-11 w-full rounded-xl border border-[var(--border)] bg-[var(--surface)] px-3.5 text-[0.95rem] font-semibold text-[var(--ink)] focus-visible:border-[var(--accent)]";

function ErrorNote({ children }) {
  if (!children) return null;
  return (
    <p role="alert" className="rounded-xl border border-red-200 bg-[var(--bad-soft)] px-3.5 py-2.5 text-sm font-semibold text-[var(--bad-ink)]">
      {children}
    </p>
  );
}

function Stepper({ step }) {
  const at = step === "done" ? C.STEPS.length : C.STEPS.findIndex((s) => s.id === step);
  return (
    <ol className="flex items-center gap-2 text-xs font-bold" aria-label="Progress">
      {C.STEPS.map((s, i) => {
        const done = i < at;
        const now = i === at;
        return (
          <li key={s.id} aria-current={now ? "step" : undefined} className="flex items-center gap-2">
            <span
              className={`flex h-6 w-6 items-center justify-center rounded-full text-[0.6875rem] tabular-nums ring-1 ring-inset ${
                done ? "bg-[var(--accent)] text-white ring-[var(--accent)]" : now ? "bg-[var(--accent-soft)] text-[var(--accent-strong)] ring-[var(--accent)]" : "text-[var(--muted)] ring-[var(--border-strong)]"
              }`}
            >
              {done ? <Icon name="check" className="h-3.5 w-3.5" strokeWidth={3} /> : i + 1}
            </span>
            <span className={`hidden sm:inline ${now ? "text-[var(--ink)]" : "text-[var(--muted)]"}`}>{s.label}</span>
            {i < C.STEPS.length - 1 && <span className="h-px w-4 bg-[var(--border-strong)] sm:w-8" aria-hidden />}
          </li>
        );
      })}
    </ol>
  );
}

// One question with options as radio chips. Native radios: arrow keys move within the group, Tab leaves it.
function Choice({ legend, name, options, value, onChange, columns = "sm:grid-cols-2" }) {
  return (
    <fieldset className="min-w-0">
      <legend className="text-sm font-bold text-[var(--ink)]">{legend}</legend>
      <div className={`mt-2 grid gap-2 ${columns}`}>
        {options.map(([v, label]) => (
          <label
            key={v}
            className="press flex min-h-11 cursor-pointer items-center gap-2.5 rounded-xl border border-[var(--border)] bg-[var(--surface)] px-3.5 py-2 text-sm font-semibold text-[var(--ink-secondary)] has-[:checked]:border-[var(--accent)] has-[:checked]:bg-[var(--accent-soft)] has-[:checked]:text-[var(--accent-strong)] has-[:focus-visible]:outline has-[:focus-visible]:outline-2 has-[:focus-visible]:outline-offset-2 has-[:focus-visible]:outline-[var(--accent)]"
          >
            <input type="radio" name={name} value={String(v)} checked={String(value) === String(v)} onChange={() => onChange(String(v))} className="h-4 w-4 accent-[var(--accent)]" />
            {label}
          </label>
        ))}
      </div>
    </fieldset>
  );
}

// A statement answered on the five-point agree scale: five radios in a row (a column on a phone).
function Statement({ index, text, name, value, onChange }) {
  return (
    <fieldset className="min-w-0 border-t border-[var(--border)] py-4 first:border-t-0 first:pt-0">
      <legend className="flex gap-2.5 text-sm font-semibold leading-relaxed text-[var(--ink)]">
        <span className="t-index pt-0.5 text-[var(--muted)]">{String(index).padStart(2, "0")}</span>
        <span>{text}</span>
      </legend>
      <div className="mt-2.5 grid grid-cols-5 gap-1.5 sm:gap-2">
        {C.LIKERT.map((label, i) => (
          <label
            key={label}
            className="press flex min-h-11 cursor-pointer flex-col items-center justify-center gap-1 rounded-lg border border-[var(--border)] bg-[var(--surface)] px-1 py-1.5 text-center text-[0.6875rem] font-semibold leading-tight text-[var(--ink-secondary)] has-[:checked]:border-[var(--accent)] has-[:checked]:bg-[var(--accent-soft)] has-[:checked]:text-[var(--accent-strong)] has-[:focus-visible]:outline has-[:focus-visible]:outline-2 has-[:focus-visible]:outline-offset-2 has-[:focus-visible]:outline-[var(--accent)]"
          >
            <input type="radio" name={name} value={String(i + 1)} checked={String(value) === String(i + 1)} onChange={() => onChange(String(i + 1))} className="h-4 w-4 accent-[var(--accent)]" />
            <span className="hidden sm:block">{label}</span>
            <span className="sm:hidden" aria-label={label}>{i + 1}</span>
          </label>
        ))}
      </div>
    </fieldset>
  );
}

// ---------------------------------------------------------------------------------------------------------------- the steps

function Consent({ onDone }) {
  const [agree, setAgree] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const go = async () => {
    setBusy(true);
    setError("");
    try {
      const r = await api.studyEnroll();
      onDone(r.code);
    } catch (e) {
      setError(e.status === 0 ? "The server did not answer. If it was asleep, give it a few seconds and try again." : e.message);
    } finally {
      setBusy(false);
    }
  };
  return (
    <section aria-labelledby="study-h">
      <h1 id="study-h" tabIndex={-1} className="t-h2 text-[var(--ink)] focus:outline-none">{C.CONSENT.title}</h1>
      <p className="mt-3 text-base font-medium leading-relaxed text-[var(--ink-secondary)]">{C.CONSENT.lead}</p>
      <dl className="mt-6 divide-y divide-[var(--border)] rounded-2xl border border-[var(--border)] bg-[var(--surface)]">
        {C.CONSENT.points.map(([k, v]) => (
          <div key={k} className="grid gap-1 px-5 py-4 sm:grid-cols-[9rem_minmax(0,1fr)] sm:gap-6">
            <dt className="text-sm font-extrabold text-[var(--ink)]">{k}</dt>
            <dd className="text-sm font-medium leading-relaxed text-[var(--ink-secondary)]">{v}</dd>
          </div>
        ))}
      </dl>
      {C.ETHICS_REFERENCE && <p className="mt-3 text-xs font-semibold text-[var(--muted)]">Ethics reference: {C.ETHICS_REFERENCE}</p>}
      <label className="press mt-6 flex cursor-pointer items-start gap-3 rounded-xl border border-[var(--border)] bg-[var(--surface)] px-4 py-3.5 text-sm font-bold text-[var(--ink)] has-[:checked]:border-[var(--accent)] has-[:checked]:bg-[var(--accent-soft)] has-[:focus-visible]:outline has-[:focus-visible]:outline-2 has-[:focus-visible]:outline-offset-2 has-[:focus-visible]:outline-[var(--accent)]">
        <input type="checkbox" checked={agree} onChange={(e) => setAgree(e.target.checked)} className="mt-0.5 h-5 w-5 shrink-0 accent-[var(--accent)]" aria-required="true" />
        {C.CONSENT.agree}
      </label>
      <div className="mt-4 space-y-3">
        <ErrorNote>{error}</ErrorNote>
        <Button variant="primary" size="lg" icon={busy ? "loader" : "check"} disabled={!agree || busy} onClick={go}>
          {busy ? "Getting your code…" : "I agree: get my participant code"}
        </Button>
        {!agree && <p className="text-xs font-medium text-[var(--muted)]">Tick the box to continue.</p>}
      </div>
    </section>
  );
}

function CodeBadge({ code }) {
  return (
    <div className="flex flex-wrap items-center gap-3 rounded-2xl border border-[var(--accent-ring)] bg-[var(--accent-soft)] px-5 py-4">
      <div>
        <p className="t-kicker text-[var(--accent-strong)]">Your participant code</p>
        <p className="mt-1 text-4xl font-extrabold tracking-tight tabular-nums text-[var(--ink)]" aria-label={`Your code is ${code.split("").join(" ")}`}>
          {code}
        </p>
      </div>
      <Button className="ml-auto" icon="copy" onClick={() => copyText(code, "Code")}>
        Copy code
      </Button>
    </div>
  );
}

function Pre({ code, onDone }) {
  const [a, setA] = useState({});
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const missing = preMissing(a);
  useEffect(() => setError(""), [a]); // an old complaint goes away as soon as an answer changes
  const submit = async (e) => {
    e.preventDefault();
    if (missing.length) return setError(`Please answer: ${missing.join(", ")}.`);
    setBusy(true);
    setError("");
    try {
      await api.studyPre(code, a);
      onDone();
    } catch (err) {
      if (err.status === 409) return onDone(); // already sent (a second tab): carry on
      setError(err.message);
    } finally {
      setBusy(false);
    }
  };
  return (
    <form onSubmit={submit} noValidate aria-labelledby="study-h" className="space-y-6">
      <div>
        <h1 id="study-h" tabIndex={-1} className="t-h2 text-[var(--ink)] focus:outline-none">Three quick questions</h1>
        <p className="mt-3 text-base font-medium leading-relaxed text-[var(--ink-secondary)]">So the results can be read in context. Nothing here identifies you.</p>
      </div>
      <CodeBadge code={code} />
      <Choice legend="Your age" name="ageBand" options={C.AGE_BANDS} value={a.ageBand} onChange={(v) => setA({ ...a, ageBand: v })} columns="grid-cols-2 sm:grid-cols-4" />
      <Choice legend="Your experience with VR headsets" name="vrExperience" options={C.VR_EXPERIENCE} value={a.vrExperience} onChange={(v) => setA({ ...a, vrExperience: v })} />
      <Choice legend="Exercise you usually do in a week" name="weeklyExercise" options={C.WEEKLY_EXERCISE} value={a.weeklyExercise} onChange={(v) => setA({ ...a, weeklyExercise: v })} columns="grid-cols-2 sm:grid-cols-3" />
      <ErrorNote>{error}</ErrorNote>
      <Button type="submit" variant="primary" size="lg" icon={busy ? "loader" : "enter"} disabled={busy}>
        {busy ? "Saving…" : "Continue to the game"}
      </Button>
    </form>
  );
}

function Play({ code, status }) {
  return (
    <section aria-labelledby="study-h" className="space-y-6">
      <div>
        <h1 id="study-h" tabIndex={-1} className="t-h2 text-[var(--ink)] focus:outline-none">Now play</h1>
        <p className="mt-3 text-base font-medium leading-relaxed text-[var(--ink-secondary)]">
          Put the headset on and pick your player name. Use your code exactly as shown: that is how the game, and later this page, know the fight is yours.
        </p>
      </div>
      <CodeBadge code={code} />
      <ol className="space-y-3 rounded-2xl border border-[var(--border)] bg-[var(--surface)] p-5 text-sm font-medium text-[var(--ink-secondary)]">
        <li className="flex gap-3"><span className="t-index text-[var(--accent)]">01</span> In the name picker choose or type <strong className="text-[var(--ink)]">{code}</strong>.</li>
        <li className="flex gap-3"><span className="t-index text-[var(--accent)]">02</span> Play one full fight, to the end: a win, a loss or running out of time all count.</li>
        <li className="flex gap-3"><span className="t-index text-[var(--accent)]">03</span> Take the headset off and come back to this page. It opens the last form by itself.</li>
      </ol>
      <p role="status" className="flex items-center gap-2.5 rounded-xl bg-[var(--surface-muted)] px-4 py-3 text-sm font-semibold text-[var(--ink-secondary)]">
        {status.fights.live ? (
          <>
            <span className="status-live h-2 w-2 rounded-full bg-[var(--good)]" aria-hidden /> A fight as {code} is being played right now.
          </>
        ) : (
          <>
            <span className="h-2 w-2 rounded-full bg-[var(--border-strong)]" aria-hidden /> Waiting for your first finished fight as {code}. This page checks every few seconds.
          </>
        )}
      </p>
    </section>
  );
}

function Post({ code, status, onDone }) {
  const [a, setA] = useState({ enjoyment: [], sus: [] });
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const set = (k, v) => setA((s) => ({ ...s, [k]: v }));
  const setAt = (k, i, v) => setA((s) => ({ ...s, [k]: Object.assign([...Array.from({ length: k === "sus" ? 10 : 5 }, (_, j) => s[k][j] || "")], { [i]: v }) }));
  const missing = postMissing(a);
  useEffect(() => setError(""), [a]);
  const submit = async (e) => {
    e.preventDefault();
    if (missing.length) return setError(`Please answer: ${missing.join("; ")}.`);
    setBusy(true);
    setError("");
    try {
      // The Borg RPE goes through the same endpoint as any fight rating; "already rated" (a retry) is fine.
      try {
        await api.submitFeedback(status.fights.latestFinishedId, { rpe: Number(a.rpe) });
      } catch (err) {
        if (err.status !== 409) throw err;
      }
      try {
        await api.studyPost(code, postBody(a));
      } catch (err) {
        if (err.status !== 409) throw err;
      }
      onDone();
    } catch (err) {
      setError(err.message);
    } finally {
      setBusy(false);
    }
  };
  return (
    <form onSubmit={submit} noValidate aria-labelledby="study-h" className="space-y-8">
      <div>
        <h1 id="study-h" tabIndex={-1} className="t-h2 text-[var(--ink)] focus:outline-none">How was it?</h1>
        <p className="mt-3 text-base font-medium leading-relaxed text-[var(--ink-secondary)]">About three minutes. Answer for the game you just played; there are no right answers.</p>
      </div>

      <section aria-labelledby="q-borg" className="space-y-2">
        <h2 id="q-borg" className="text-base font-extrabold text-[var(--ink)]">1 · How hard did the fight feel?</h2>
        <p className="text-sm font-medium text-[var(--muted)]">Borg scale: 6 is no effort at all, 20 is the hardest you could possibly work. Think about your whole body, not just your arms.</p>
        <select aria-label="Perceived exertion, Borg 6 to 20" value={a.rpe || ""} onChange={(e) => set("rpe", e.target.value)} className={field}>
          <option value="">Choose a number…</option>
          {C.BORG.map(([v, l]) => (
            <option key={v} value={v}>{l}</option>
          ))}
        </select>
      </section>

      <section aria-labelledby="q-enj" className="space-y-3">
        <h2 id="q-enj" className="text-base font-extrabold text-[var(--ink)]">2 · Your enjoyment</h2>
        <div>
          {C.ENJOYMENT_ITEMS.map((t, i) => (
            <Statement key={t} index={i + 1} text={t} name={`enj-${i}`} value={a.enjoyment[i]} onChange={(v) => setAt("enjoyment", i, v)} />
          ))}
        </div>
      </section>

      <section aria-labelledby="q-eff" className="space-y-3">
        <h2 id="q-eff" className="text-base font-extrabold text-[var(--ink)]">3 · Compared with your usual workouts</h2>
        <Choice legend="The effort in this game was…" name="effort" options={C.EFFORT_VS_USUAL} value={a.effortVsUsual} onChange={(v) => set("effortVsUsual", v)} columns="sm:grid-cols-1" />
      </section>

      <section aria-labelledby="q-again" className="space-y-3">
        <h2 id="q-again" className="text-base font-extrabold text-[var(--ink)]">4 · Would you play again this week?</h2>
        <Choice legend="Would you play this game again in the coming week?" name="again" options={C.PLAY_AGAIN} value={a.playAgain} onChange={(v) => set("playAgain", v)} columns="grid-cols-3" />
      </section>

      <section aria-labelledby="q-sus" className="space-y-3">
        <h2 id="q-sus" className="text-base font-extrabold text-[var(--ink)]">5 · Using the game</h2>
        <div>
          {C.SUS_ITEMS.map((t, i) => (
            <Statement key={t} index={i + 1} text={t} name={`sus-${i}`} value={a.sus[i]} onChange={(v) => setAt("sus", i, v)} />
          ))}
        </div>
      </section>

      <div className="space-y-3">
        <ErrorNote>{error}</ErrorNote>
        <Button type="submit" variant="primary" size="lg" icon={busy ? "loader" : "check"} disabled={busy}>
          {busy ? "Sending…" : "Send my answers"}
        </Button>
        {missing.length > 0 && <p className="text-xs font-medium text-[var(--muted)]">Still to answer: {missing.join("; ")}.</p>}
      </div>
    </form>
  );
}

function Done({ code, onFinish }) {
  return (
    <section aria-labelledby="study-h" className="space-y-5 text-center">
      <span className="mx-auto flex h-14 w-14 items-center justify-center rounded-2xl bg-[var(--good-soft)] text-[var(--good-ink)]">
        <Icon name="check" className="h-7 w-7" strokeWidth={2.4} />
      </span>
      <h1 id="study-h" tabIndex={-1} className="t-h2 text-[var(--ink)] focus:outline-none">Thank you</h1>
      <p className="mx-auto max-w-md text-base font-medium leading-relaxed text-[var(--ink-secondary)]">
        Your answers are saved under <strong className="text-[var(--ink)]">{code}</strong>. If you ever want them deleted, tell the researcher that code.
      </p>
      <div className="flex flex-wrap items-center justify-center gap-3">
        <Link to="/" className="press inline-flex items-center gap-2 rounded-xl border border-[var(--border)] bg-[var(--surface)] px-4 py-2.5 text-sm font-bold text-[var(--accent)] hover:underline">
          About STRIKEFIT
        </Link>
        <button type="button" onClick={onFinish} className="press rounded-lg px-3 py-2 text-sm font-semibold text-[var(--muted)] hover:text-[var(--ink)]">
          Clear this browser (shared device)
        </button>
      </div>
    </section>
  );
}

// ---------------------------------------------------------------------------------------------------------------- the page

export default function StudyView() {
  const [code, setCode] = useState(store.get);
  const [status, setStatus] = useState(null);
  const [loading, setLoading] = useState(Boolean(store.get()));
  const [problem, setProblem] = useState("");
  const { toasts, dismiss } = useToasts();

  const refresh = useCallback(async () => {
    if (!code) return;
    try {
      setStatus(await api.studyStatus(code));
      setProblem("");
    } catch (e) {
      if (e.status === 404) {
        store.set(null);
        setCode(null);
        setStatus(null);
      } else setProblem("Could not reach the server. It may be waking up; this page will keep trying.");
    } finally {
      setLoading(false);
    }
  }, [code]);

  useEffect(() => {
    document.title = "Playtest study · STRIKEFIT";
    return () => {
      document.title = "STRIKEFIT · Swing harder, hit harder";
    };
  }, []);
  useEffect(() => {
    refresh();
  }, [refresh]);

  const step = loading ? "loading" : stepFor(code ? status : null);
  // While waiting for the fight (and for a failed check) look again every few seconds.
  useEffect(() => {
    if (step !== "play" && !problem) return undefined;
    const t = setInterval(refresh, 4000);
    return () => clearInterval(t);
  }, [step, problem, refresh]);
  // Each step has its own heading: move focus to it so a screen reader announces the new step.
  useEffect(() => {
    document.getElementById("study-h")?.focus?.();
  }, [step]);

  const enrolled = (c) => {
    store.set(c);
    setCode(c);
    setLoading(true);
    toast.good("Your code is " + c, "Write it down; you will type it into the headset.");
  };
  const finish = () => {
    store.set(null);
    setCode(null);
    setStatus(null);
  };

  return (
    <div className="min-h-screen bg-[var(--bg)] text-[var(--ink)]">
      <a href="#study-main" className="sr-only focus:not-sr-only focus:fixed focus:left-3 focus:top-3 focus:z-50 focus:rounded-lg focus:bg-[var(--surface)] focus:px-3 focus:py-2 focus:font-semibold focus:shadow-lg">
        Skip to the form
      </a>
      <header className="border-b border-[var(--border)] bg-[var(--surface)]">
        <div className="mx-auto flex max-w-2xl flex-wrap items-center justify-between gap-x-6 gap-y-3 px-4 py-3.5 sm:px-6">
          <Link to="/" aria-label="STRIKEFIT home" className="rounded-xl">
            <Brand />
          </Link>
          {step !== "loading" && <Stepper step={step} />}
        </div>
      </header>
      <main id="study-main" className="mx-auto max-w-2xl px-4 py-10 sm:px-6 sm:py-14">
        {problem && <div className="mb-6"><ErrorNote>{problem}</ErrorNote></div>}
        {step === "loading" ? (
          <div className="space-y-4" aria-busy="true" aria-label="Loading">
            <div className="skeleton h-9 w-2/3 rounded-lg" />
            <div className="skeleton h-24 w-full rounded-2xl" />
            <div className="skeleton h-40 w-full rounded-2xl" />
          </div>
        ) : step === "consent" ? (
          <Consent onDone={enrolled} />
        ) : step === "pre" ? (
          <Pre code={code} onDone={refresh} />
        ) : step === "play" ? (
          <Play code={code} status={status} />
        ) : step === "post" ? (
          <Post code={code} status={status} onDone={refresh} />
        ) : (
          <Done code={code} onFinish={finish} />
        )}
      </main>
      <footer className="mx-auto max-w-2xl px-4 pb-10 text-xs font-medium text-[var(--muted)] sm:px-6">STRIKEFIT · VR fitness research prototype · <Link to="/about/method" className="underline hover:text-[var(--accent)]">How the study works</Link></footer>
      <ToastStack toasts={toasts} onDismiss={dismiss} />
    </div>
  );
}
