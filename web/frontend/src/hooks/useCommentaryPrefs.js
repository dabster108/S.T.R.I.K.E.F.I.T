import { useSyncExternalStore } from "react";
import { commentaryPrefs } from "../lib/commentaryPrefs.js";

// The viewer's commentary choices ({ hidden, speech }), kept apart from the commentary logic so a page that only shows the two switches
// (the quiet Live Arena) does not download the logic with them.
export const useCommentaryPrefs = () => useSyncExternalStore(commentaryPrefs.subscribe, commentaryPrefs.get);
