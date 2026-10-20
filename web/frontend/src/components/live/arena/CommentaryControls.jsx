import { useCommentaryPrefs } from "../../../hooks/useCommentaryPrefs.js";
import { commentaryPrefs, speechSupported } from "../../../lib/commentaryPrefs.js";
import Button from "../../ui/Button.jsx";

/** The viewer's two choices. The voice is the browser's own (SpeechSynthesis), off until it is switched on. A small module of its own: the quiet Live Arena shows these before any fight. */
export default function CommentaryControls() {
  const prefs = useCommentaryPrefs();
  const canSpeak = speechSupported();
  return (
    <div className="flex flex-wrap items-center gap-2" role="group" aria-label="Commentary settings">
      <Button icon="captions" onClick={() => commentaryPrefs.setHidden(!prefs.hidden)} aria-pressed={!prefs.hidden} title={prefs.hidden ? "Show the live commentary" : "Hide the live commentary"}>
        Commentary: {prefs.hidden ? "off" : "on"}
      </Button>
      <Button
        icon={prefs.speech && canSpeak ? "volume" : "volumeOff"}
        onClick={() => commentaryPrefs.setSpeech(!prefs.speech)}
        aria-pressed={prefs.speech && canSpeak}
        disabled={!canSpeak || prefs.hidden}
        title={canSpeak ? "Read the commentary of the first fight aloud, with this browser's own voice" : "This browser has no speech synthesis"}
      >
        Spoken commentary: {prefs.speech && canSpeak ? "on" : "off"}
      </Button>
    </div>
  );
}
