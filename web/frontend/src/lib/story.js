// The shared story content (content/story.json, synced into src/content) bound for the app: guardians, interludes and the level
// lines, read in one place so the landing page, /story and /monsters never copy text.
import story from "../content/story.json";
import { makeStory } from "./storyData.js";

const s = makeStory(story);
export const { slides, levels, guardians, interludes, guardianForLevel, guardianById, levelFor, guardianForKey } = s;
export default story;
