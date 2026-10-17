// Pure helpers over the shared story content (content/story.json). No JSON import here so node --test can feed it the file;
// story.js binds it to the synced copy for the app. Every helper tolerates a file without the newer fields.
const asList = (v) => (Array.isArray(v) ? v : []);

export function makeStory(story) {
  const levels = asList(story.levels);
  const guardians = asList(story.guardians);
  const interludes = {
    afterLevel1: story.interludes?.afterLevel1 || "",
    afterLevel2: story.interludes?.afterLevel2 || "",
    ending: asList(story.interludes?.ending),
    defeat: story.interludes?.defeat || "",
  };
  const guardianForLevel = (n) => guardians.find((g) => g.level === Number(n)) || null;
  return {
    story,
    slides: asList(story.slides),
    levels,
    guardians,
    interludes,
    guardianForLevel,
    guardianById: (id) => guardians.find((g) => g.id === id) || null,
    levelFor: (n) => levels.find((l) => l.level === Number(n)) || null,
    // Backend level keys ("Easy" / "Medium" / "Hard") to the guardian.
    guardianForKey: (key) => {
      const lv = levels.find((l) => l.key === key);
      return lv ? guardianForLevel(lv.level) : null;
    },
  };
}
