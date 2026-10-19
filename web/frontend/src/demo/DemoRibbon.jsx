import { setDemo } from "./mode.js";

// The always-visible "Demo data" ribbon: bottom left, above everything but the browser. Says what is going on and offers the way out.
export default function DemoRibbon() {
  return (
    <div role="status" className="demo-ribbon fixed bottom-3 left-3 z-[70] flex max-w-[calc(100vw-1.5rem)] items-center gap-2.5 rounded-full border border-amber-300 bg-amber-100 py-1.5 pl-3 pr-1.5 text-xs font-bold text-amber-950 shadow-lg">
      <span className="h-2 w-2 shrink-0 rounded-full bg-amber-600" aria-hidden />
      <span className="truncate">
        Demo data <span className="hidden font-semibold text-amber-900 sm:inline">· scripted, no server needed</span>
      </span>
      <button type="button" onClick={() => setDemo(false)} className="press shrink-0 rounded-full bg-amber-950 px-2.5 py-1 text-[0.6875rem] font-extrabold text-amber-50 hover:bg-black">
        Exit demo
      </button>
    </div>
  );
}
