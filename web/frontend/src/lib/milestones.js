// Words and small helpers for the milestone timeline (tested in milestones.test.js). The milestones themselves come from the API.

const MONTHS = ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];

/** "15 Oct 2026" from the API's Kathmandu calendar day (YYYY-MM-DD). A calendar day, so no time zone is applied to it. */
export function milestoneDate(day) {
  const m = /^(\d{4})-(\d{2})-(\d{2})$/.exec(String(day || ""));
  return m ? `${Number(m[3])} ${MONTHS[Number(m[2]) - 1]} ${m[1]}` : "";
}

/** A progress fraction (0 to 1) as a whole percent, kept between 0 and 100; anything that is not a number is 0. */
export const progressPercent = (p) => (Number.isFinite(p) ? Math.max(0, Math.min(100, Math.round(p * 100))) : 0);

/** One sentence for a screen reader: "Reached 15 Oct 2026" or "62% of the way". */
export const milestoneStatus = (m) => (m.reachedAt ? `Reached ${milestoneDate(m.day)}` : `${progressPercent(m.progress)}% of the way`);

/** True when there is anything to draw (a player with no fights has neither reached nor upcoming ones to show as a timeline). */
export const hasTimeline = (data) => Boolean(data && ((data.achieved || []).length || (data.upcoming || []).length));
