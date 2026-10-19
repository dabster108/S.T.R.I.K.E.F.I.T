import { api } from "./api.js";
import { toast } from "./toast.js";

// Downloads one of the server's CSV exports and says so in a toast. Used by the command palette; the Export page keeps its own
// richer status line. Never throws: a failure becomes a toast.
export async function exportCsv(kind, filters = {}) {
  const waiting = toast.push({ tone: "info", timeout: 0, title: `Preparing ${kind}.csv…` });
  try {
    const res = await fetch(api.exportUrl(kind, filters));
    if (!res.ok) throw new Error(`the server answered ${res.status}`);
    const blob = await res.blob();
    const rows = Number(res.headers.get("X-Row-Count"));
    const name = /filename="([^"]+)"/.exec(res.headers.get("Content-Disposition") || "")?.[1] || `strikefit-${kind}.csv`;
    const url = URL.createObjectURL(blob);
    const a = Object.assign(document.createElement("a"), { href: url, download: name });
    document.body.appendChild(a);
    a.click();
    a.remove();
    setTimeout(() => URL.revokeObjectURL(url), 5000);
    toast.dismiss(waiting);
    toast.good("CSV downloaded", `${name}${Number.isFinite(rows) ? ` · ${rows.toLocaleString()} rows` : ""}`);
    return true;
  } catch (err) {
    toast.dismiss(waiting);
    toast.bad("Export failed", err.message === "Failed to fetch" ? "the server didn't answer" : err.message);
    return false;
  }
}
