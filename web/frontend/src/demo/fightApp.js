import { makeFightBuilder } from "./fightScript.js";
import attackTable from "../content/monsterAttacks.json";

// fightScript.js bound to the real monster attack table (wind-ups and categories read off the prefabs).
export const buildFight = makeFightBuilder({ attackTable });
