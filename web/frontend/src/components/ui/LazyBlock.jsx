import { useEffect, useState } from "react";
import { useInView } from "../../hooks/motion.js";
import { TOUR_EVENT } from "../onboarding/tourState.js";

// Mounts its children only once it is within `margin` of the viewport (or the guided tour starts, which points at things inside),
// and until then holds `minHeight` px so the page below does not move when the content lands. For content under the fold whose
// code and data should not compete with the first screen. Once mounted it stays mounted.
export default function LazyBlock({ children, fallback = null, minHeight = 400, margin = "400px" }) {
  const [ref, near] = useInView({ rootMargin: `${margin} 0px ${margin} 0px` });
  const [forced, setForced] = useState(false);
  useEffect(() => {
    const open = () => setForced(true);
    window.addEventListener(TOUR_EVENT, open);
    return () => window.removeEventListener(TOUR_EVENT, open);
  }, []);
  const show = near || forced;
  return (
    <div ref={ref} style={show ? undefined : { minHeight }}>
      {show ? children : fallback}
    </div>
  );
}
