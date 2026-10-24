import { LiveProvider } from "../live/LiveProvider.jsx";
import BroadcastView from "./BroadcastView.jsx";

// The public broadcast page (/broadcast, /watch) with the live stream its stats overlay listens to: lazy-loaded as one chunk, so
// the landing page never downloads the live store.
export default function BroadcastRoute() {
  return (
    <LiveProvider>
      <BroadcastView />
    </LiveProvider>
  );
}
