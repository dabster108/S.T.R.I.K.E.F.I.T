// Where the visitor is inside /dashboard, as a trail: Players > Ada, Sessions > Session 3f9a1c2e. Pure; the Topbar renders it.
export const TITLES = {
  "": "Overview",
  live: "Live Arena",
  players: "Players",
  player: "Player",
  sessions: "Sessions",
  session: "Session",
  leaderboard: "Leaderboard",
  compare: "Compare",
  analytics: "Analytics",
  lab: "Thesis Lab",
  engagement: "Engagement",
  export: "Export",
  guide: "User guide",
  connection: "Connection",
  broadcast: "Broadcast",
  story: "Story",
};

// A detail page's parent list.
const PARENT = { player: "players", session: "sessions" };

const decode = (s) => {
  try {
    return decodeURIComponent(s);
  } catch {
    return s;
  }
};

// [{ label, to? }]: the last crumb has no `to` (it is the page you are on).
export function breadcrumbs(pathname) {
  const [section = "", detail] = pathname.replace(/^\/dashboard\/?/, "").split("/").filter((x, i) => i < 2 || x);
  const crumbs = [{ label: "Dashboard", to: "/dashboard" }];
  if (section === "") return [{ label: TITLES[""] }];
  if (!(section in TITLES)) return [...crumbs, { label: "Page not found" }];
  if (PARENT[section]) {
    crumbs.push({ label: TITLES[PARENT[section]], to: `/dashboard/${PARENT[section]}` });
    const name = detail ? decode(detail) : "";
    crumbs.push({ label: section === "session" && name ? `Session ${name.slice(0, 8)}` : name || TITLES[section] });
    return crumbs;
  }
  crumbs.push({ label: TITLES[section] });
  return crumbs;
}
