import { useEffect, useState } from "react";

// Measures a container so SVG charts draw at real pixel size (crisp text, no stretching).
// Returns a callback ref, so it also works when the element mounts after first render.
export function useElementWidth(fallback = 600) {
  const [el, setEl] = useState(null);
  const [width, setWidth] = useState(fallback);

  useEffect(() => {
    if (!el) return undefined;
    const ro = new ResizeObserver(([entry]) => {
      const w = Math.round(entry.contentRect.width);
      if (w > 0) setWidth(w);
    });
    ro.observe(el);
    return () => ro.disconnect();
  }, [el]);

  return [setEl, width];
}
