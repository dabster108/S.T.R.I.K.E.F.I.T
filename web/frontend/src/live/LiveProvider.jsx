import { useEffect, useMemo, useRef, useSyncExternalStore } from "react";
import { liveStore, startLive } from "./liveStore.js";

// Mount once near the root: opens the single shared stream.
export function LiveProvider({ children }) {
  useEffect(() => startLive(), []);
  return children;
}

export function useConnection() {
  const connection = useSyncExternalStore(liveStore.subscribeConnection, liveStore.getConnection);
  const mode = useSyncExternalStore(liveStore.subscribeConnection, liveStore.getMode);
  return { connection, mode };
}

export function useLiveIds() {
  return useSyncExternalStore(liveStore.subscribeIds, liveStore.getIds);
}

export function useLiveReady() {
  return useSyncExternalStore(liveStore.subscribeIds, liveStore.isReady);
}

export function useLiveSession(id) {
  const subscribe = useMemo(() => liveStore.subscribeSession(id), [id]);
  return useSyncExternalStore(subscribe, () => liveStore.getSession(id));
}

// Live entries for one player (their current fight, if any).
export function useLiveForPlayer(name) {
  const ids = useLiveIds();
  const match = ids.find((id) => {
    const e = liveStore.getSession(id);
    return e && e.session.playerName === name && !e.finishedAt;
  });
  return useLiveSession(match || "__none__");
}

export function useFeed(filter) {
  const feed = useSyncExternalStore(liveStore.subscribeFeed, liveStore.getFeed);
  return useMemo(() => (filter ? feed.filter(filter) : feed), [feed, filter]);
}

// Calls handler(type, payload) for every stream message; handler may change freely.
export function useLiveMessages(handler) {
  const ref = useRef(handler);
  ref.current = handler;
  useEffect(() => liveStore.onMessage((type, payload) => ref.current(type, payload)), []);
}
