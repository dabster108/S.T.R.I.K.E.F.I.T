// A stand-in for EventSource that the demo installs in place of the real one: the live store opens it exactly as it opens /api/stream,
// and gets the scripted fights' messages (and a heartbeat) from the demo engine.

export function createFakeEventSource(engine, { heartbeatMs = 15000 } = {}) {
  const sources = new Set();
  let timer = null;

  const pump = () => {
    clearTimeout(timer);
    if (!sources.size) return;
    const wait = engine.advance(dispatch);
    timer = setTimeout(pump, wait ?? 1000);
  };
  function dispatch(type, payload) {
    for (const s of sources) s._deliver(type, payload);
  }

  class FakeEventSource {
    constructor(url) {
      this.url = url;
      this.readyState = 0;
      this._listeners = new Map();
      this.onopen = null;
      this.onerror = null;
      this._hb = null;
      sources.add(this);
      setTimeout(() => {
        if (!sources.has(this)) return;
        this.readyState = 1;
        this.onopen?.({});
        this._deliver("hello", { demo: true });
        this._hb = setInterval(() => this._deliver("heartbeat", { at: new Date().toISOString() }), heartbeatMs);
        pump();
      }, 30);
    }
    addEventListener(type, fn) {
      if (!this._listeners.has(type)) this._listeners.set(type, new Set());
      this._listeners.get(type).add(fn);
    }
    removeEventListener(type, fn) {
      this._listeners.get(type)?.delete(fn);
    }
    _deliver(type, payload) {
      const data = JSON.stringify({ payload });
      for (const fn of this._listeners.get(type) || []) fn({ data });
    }
    close() {
      this.readyState = 2;
      clearInterval(this._hb);
      sources.delete(this);
      if (!sources.size) clearTimeout(timer);
    }
  }
  FakeEventSource.CONNECTING = 0;
  FakeEventSource.OPEN = 1;
  FakeEventSource.CLOSED = 2;
  return FakeEventSource;
}
