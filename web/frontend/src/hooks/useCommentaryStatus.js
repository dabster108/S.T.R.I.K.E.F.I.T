import { useEffect, useState } from "react";
import { aiStatus } from "../lib/commentaryAi.js";

const OFF = { ai: false, voice: false };

/** { ai, voice }: whether the server has the AI commentator and Google's voice switched on. Off until the server has answered. */
export function useCommentaryStatus() {
  const [status, setStatus] = useState(OFF);
  useEffect(() => {
    let alive = true;
    aiStatus().then((s) => alive && setStatus(s));
    return () => {
      alive = false;
    };
  }, []);
  return status;
}
