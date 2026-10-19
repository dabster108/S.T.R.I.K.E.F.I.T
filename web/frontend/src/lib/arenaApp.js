// The arena helpers bound to the real content files (see arena.js for why they are separate).
import attackTable from "../content/monsterAttacks.json";
import { makeArena } from "./arena.js";
import { guardianForKey } from "./story.js";

export const { categoryLabel, monsterFor, attackInfo, reduceArena, killFeedItem } = makeArena({ attackTable, guardianForKey });
