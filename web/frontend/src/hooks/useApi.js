import { useCallback, useEffect, useRef, useState } from "react";

// Fetches on mount and whenever deps change; keeps the previous data while
// refetching so views don't flash. `reload()` refetches on demand.
export function useApi(fetcher, deps = []) {
  const [state, setState] = useState({ data: undefined, error: null, loading: true });
  const seq = useRef(0);
  const fetchRef = useRef(fetcher);
  fetchRef.current = fetcher;

  const load = useCallback(async () => {
    const id = ++seq.current;
    setState((s) => ({ ...s, loading: true }));
    try {
      const data = await fetchRef.current();
      if (id === seq.current) setState({ data, error: null, loading: false });
    } catch (error) {
      if (id === seq.current) setState((s) => ({ ...s, error, loading: false }));
    }
  }, []);

  // eslint-disable-next-line react-hooks/exhaustive-deps
  useEffect(() => { load(); }, deps);

  // Replaces the data in place (e.g. with a PATCH response) and drops any request in flight.
  const set = useCallback((data) => {
    ++seq.current;
    setState({ data, error: null, loading: false });
  }, []);

  return { ...state, reload: load, set };
}

// Calls fn at most once per `ms` while events keep arriving (trailing call guaranteed).
export function useThrottledCallback(fn, ms) {
  const fnRef = useRef(fn);
  fnRef.current = fn;
  const timer = useRef(null);
  const last = useRef(0);
  useEffect(() => () => clearTimeout(timer.current), []);
  return useCallback(() => {
    const wait = last.current + ms - Date.now();
    if (wait <= 0) {
      last.current = Date.now();
      fnRef.current();
    } else if (!timer.current) {
      timer.current = setTimeout(() => {
        timer.current = null;
        last.current = Date.now();
        fnRef.current();
      }, wait);
    }
  }, [ms]);
}
