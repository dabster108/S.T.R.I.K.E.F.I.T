// The milestone timeline: how each kind of milestone looks. What the milestones are, their wording, their order and their numbers come
// from the API (GET /api/players/:name/milestones, backend/config/milestones.js); only the icons are chosen here.

export const MILESTONE_ICON = Object.freeze({ fight: "swords", victory: "trophy", swing: "bolt", streak: "flame", rank: "award" });
export const FALLBACK_ICON = "sparkles";

/** How long the page waits before it asks again after a fight ends (the stream tells it when one does). */
export const MILESTONE_REFRESH_MS = 4000;
