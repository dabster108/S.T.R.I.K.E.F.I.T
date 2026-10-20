// Speaks the commentary. One voice at a time: a sentence is allowed to finish, and only the newest sentence waits behind it (an older
// one that waited too long is skipped, because the caption has moved on). The final result interrupts. Google's audio is used when there
// is some; if it is missing, blocked by the browser or cannot play, the browser's own voice reads the same sentence.
// Browser objects are passed in so the tests can use fakes.

import { COMMENTARY } from "../config/commentary.js";

export function createVoice({
  AudioCtor = typeof Audio === "undefined" ? null : Audio,
  synth = typeof window !== "undefined" && "speechSynthesis" in window ? window.speechSynthesis : null,
  Utterance = typeof SpeechSynthesisUtterance === "undefined" ? null : SpeechSynthesisUtterance,
  revoke = (u) => URL.revokeObjectURL(u),
  now = Date.now,
} = {}) {
  let audio = null; // the element playing right now
  let audioUrl = null;
  let pending = null; // { text, urlPromise, at }
  let epoch = 0;

  const free = () => {
    if (audioUrl) revoke(audioUrl);
    audio = null;
    audioUrl = null;
  };
  const dropPending = () => {
    pending?.urlPromise?.then((u) => u && revoke(u)); // its audio will never play
    pending = null;
  };
  const browserVoice = (text) => {
    if (!synth || !Utterance) return;
    synth.cancel();
    const u = new Utterance(text);
    u.rate = COMMENTARY.speechRate;
    synth.speak(u);
  };

  async function play(text, urlPromise, mine) {
    const url = urlPromise ? await urlPromise : null;
    if (mine !== epoch) {
      if (url) revoke(url);
      return;
    }
    if (url && AudioCtor) {
      const a = new AudioCtor(url);
      audio = a;
      audioUrl = url;
      const next = () => {
        if (audio !== a) return;
        free();
        const p = pending;
        if (p && now() - p.at <= COMMENTARY.ai.voiceStaleMs) {
          pending = null;
          play(p.text, p.urlPromise, epoch);
        } else dropPending();
      };
      a.onended = next;
      a.onerror = next;
      try {
        await a.play();
        return;
      } catch {
        free(); // autoplay blocked or the file would not play: the browser's voice instead
      }
    }
    if (mine === epoch) browserVoice(text);
  }

  return {
    /** Speak `text`. `urlPromise` resolves to the Google audio URL or null. `interrupt`: do not wait for the sentence being spoken. */
    say(text, urlPromise = null, { interrupt = false } = {}) {
      if (audio && !interrupt) {
        dropPending();
        pending = { text, urlPromise, at: now() };
        return;
      }
      this.stop();
      play(text, urlPromise, epoch);
    },
    stop() {
      epoch++;
      dropPending();
      if (audio) {
        audio.onended = null;
        audio.onerror = null;
        audio.pause();
      }
      free();
      synth?.cancel();
    },
    playing: () => Boolean(audio),
  };
}
