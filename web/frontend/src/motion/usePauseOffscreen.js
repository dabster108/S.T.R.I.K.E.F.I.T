import { useEffect, useState } from "react";

// Puts data-offscreen="true" on an element while it is outside the viewport (or the tab is hidden). styles.css pauses every CSS
// animation inside such an element, so a looping background, marquee or spinner costs nothing while nobody can see it.
// Returns a ref callback; the attribute is written straight to the DOM, so flipping it never re-renders React.
export function usePauseOffscreen(rootMargin = "120px") {
  const [el, setEl] = useState(null);

  useEffect(() => {
    if (!el) return undefined;
    let visible = true;
    const apply = () => el.setAttribute("data-offscreen", visible && !document.hidden ? "false" : "true");
    apply();
    const io = typeof IntersectionObserver === "undefined" ? null : new IntersectionObserver(([entry]) => {
      visible = entry.isIntersecting;
      apply();
    }, { rootMargin });
    io?.observe(el);
    document.addEventListener("visibilitychange", apply);
    return () => {
      io?.disconnect();
      document.removeEventListener("visibilitychange", apply);
    };
  }, [el, rootMargin]);

  return setEl;
}
