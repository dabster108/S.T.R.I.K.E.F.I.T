// The site's motion vocabulary, in one place. The same numbers live in CSS as --motion-* (styles/tokens.css), so a duration changed
// here or there is changed everywhere; motion.test.js fails if the two drift apart.
//
//   fast  150 ms  fades, hover feedback, route cross-fade
//   base  250 ms  popovers, small reveals, chip changes
//   slow  400 ms  entrances (Reveal, the card stagger), chart draws
//
// One easing curve for everything that moves; one stagger step; one rise distance. Only transform and opacity are ever animated.

export const DUR = Object.freeze({ fast: 150, base: 250, slow: 400 });
export const EASE = "cubic-bezier(0.22, 1, 0.36, 1)";
export const STAGGER = 60; // ms between siblings
export const RISE = 12; // px an element travels while it fades in
export const MAX_STAGGER_STEPS = 8; // past this, siblings arrive together (a long list must not take seconds)

/** Delay in ms for the i-th sibling of a staggered group. */
export const staggerDelay = (i, step = STAGGER) => Math.max(0, Math.min(Math.trunc(Number(i) || 0), MAX_STAGGER_STEPS)) * step;

/** A style object that sets the CSS variable Reveal reads. */
export const revealStyle = ({ delay = 0, index = 0 } = {}) => {
  const ms = Math.max(0, Number(delay) || 0) + staggerDelay(index);
  return ms ? { "--m-delay": `${ms}ms` } : undefined;
};
