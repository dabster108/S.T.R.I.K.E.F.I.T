import { useEffect } from "react";
import { startSpotlight } from "./spotlight.js";

// A card with a soft radial highlight that follows the cursor. Pointer devices only; off in Calm mode and under reduced motion.
// The host should clip its own content (overflow-hidden) so the highlight stays inside the rounded corners.
export default function Spotlight({ as: Tag = "div", className = "", children, ...rest }) {
  useEffect(startSpotlight, []);
  return (
    <Tag data-spotlight className={`spotlight ${className}`} {...rest}>
      {children}
    </Tag>
  );
}

// For components that render their own element (Card, StatTile): the attributes and class that make any element a spotlight host.
export const spotlightProps = { "data-spotlight": "" };
export const SPOTLIGHT_CLASS = "spotlight";
