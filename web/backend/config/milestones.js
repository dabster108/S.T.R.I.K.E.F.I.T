// Milestone timeline tunables: what counts as a milestone and the play-streak rule. The wording of each milestone is in milestones.js.

module.exports = {
  SWING_COUNTS: [100, 500, 1000], // cumulative counted swings (a session's hit count), summed in play order
  FIGHT_COUNT: 10, // finished fights, abandoned ones included (the same "fights" Wrapped counts)
  STREAK_DAYS: [3, 7], // consecutive play days
  // A play day is a calendar day (Asia/Kathmandu) with at least one finished fight. A streak is allowed to miss `forgivenMissedDays`
  // day(s) in any rolling `perDays` days without breaking: one rest day per week. progression.js uses the same rule for the 3-day
  // streak badge (7 days); test/milestones.test.js checks that the two agree.
  STREAK_RULE: { forgivenMissedDays: 1, perDays: 7 },
  UPCOMING: 3, // how many not-yet-reached milestones are returned, closest first
};
