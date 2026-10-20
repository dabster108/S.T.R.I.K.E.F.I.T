import { useReveal } from "../hooks/motion.js";
import { revealStyle } from "./tokens.js";

// Fades an element in and lifts it 12 px, once, when it first scrolls into view. Elements already scrolled past (an anchor jump)
// count as seen, so nothing is ever left invisible. Reduced motion and Calm mode show the end state at once (styles.css).
//   delay  extra ms before it starts      index  position in a staggered group (60 ms per step, capped)
export default function Reveal({ as: Tag = "div", className = "", delay = 0, index = 0, style, children, ...rest }) {
  const ref = useReveal();
  return (
    <Tag ref={ref} className={`m-reveal ${className}`} style={{ ...revealStyle({ delay, index }), ...style }} {...rest}>
      {children}
    </Tag>
  );
}
