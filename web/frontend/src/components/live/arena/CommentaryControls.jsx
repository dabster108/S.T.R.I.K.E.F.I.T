import { useCommentaryPrefs } from "../../../hooks/useCommentaryPrefs.js";
import { useCommentaryStatus } from "../../../hooks/useCommentaryStatus.js";
import { commentaryPrefs, speechSupported } from "../../../lib/commentaryPrefs.js";
import Button from "../../ui/Button.jsx";

/**
 * The viewer's choices. Commentary on/off; the AI commentator on/off (only shown when the server has it: it words each moment like a
 * football commentator, and "off" gives the plain lines); spoken commentary on/off (Google's voice when the server has it, otherwise
 * the browser's own; off until it is switched on). A small module of its own: the quiet Live Arena shows these before any fight.
 */
export default function CommentaryControls() {
  const prefs = useCommentaryPrefs();
  const status = useCommentaryStatus();
  const canSpeak = status.voice || speechSupported();
  const aiOn = status.ai && !prefs.plain;
  return (
    <div className="flex flex-wrap items-center gap-2" role="group" aria-label="Commentary settings">
      <Button icon="captions" onClick={() => commentaryPrefs.setHidden(!prefs.hidden)} aria-pressed={!prefs.hidden} title={prefs.hidden ? "Show the live commentary" : "Hide the live commentary"}>
        Commentary: {prefs.hidden ? "off" : "on"}
      </Button>
      {status.ai ? (
        <Button
          icon="sparkles"
          onClick={() => commentaryPrefs.setPlain(!prefs.plain)}
          aria-pressed={aiOn}
          disabled={prefs.hidden}
          title={aiOn ? "The AI commentator words each moment like a football match. Switch off for plain lines." : "Plain lines. Switch on for the AI football commentator."}
        >
          AI commentator: {aiOn ? "on" : "off"}
        </Button>
      ) : null}
      <Button
        icon={prefs.speech && canSpeak ? "volume" : "volumeOff"}
        onClick={() => commentaryPrefs.setSpeech(!prefs.speech)}
        aria-pressed={prefs.speech && canSpeak}
        disabled={!canSpeak || prefs.hidden}
        title={
          !canSpeak
            ? "This browser has no speech synthesis"
            : status.voice
              ? "Read the commentary of the first fight aloud, in a commentator's voice"
              : "Read the commentary of the first fight aloud, with this browser's own voice"
        }
      >
        Spoken commentary: {prefs.speech && canSpeak ? "on" : "off"}
      </Button>
    </div>
  );
}
