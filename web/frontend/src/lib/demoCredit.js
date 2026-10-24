// The demo clip's credit line: the level and its guardian (story.json), and how the footage was made. Shared by the walkthrough
// and the Level 2 card so the wording never drifts.
import { DEMO_CLIP } from "../content/showcaseShots.js";
import { levelName } from "./game.js";
import { guardianForKey } from "./story.js";

export const demoCredit = () => `${levelName(DEMO_CLIP.level)}, ${guardianForKey(DEMO_CLIP.level)?.name || "its guardian"}. ${DEMO_CLIP.note}.`;
