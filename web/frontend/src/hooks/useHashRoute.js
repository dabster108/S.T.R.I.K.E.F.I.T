import { useCallback, useEffect, useState } from "react";

// Tiny hash router: "#/sessions/abc" → { view: "sessions", param: "abc" }.
// Hash routing keeps deep links working when the backend serves the built app.
function parse() {
  const [view = "", param = null] = window.location.hash.replace(/^#\/?/, "").split("/");
  return { view: view || "dashboard", param: param ? decodeURIComponent(param) : null };
}

export function useHashRoute() {
  const [route, setRoute] = useState(parse);

  useEffect(() => {
    const onChange = () => setRoute(parse());
    window.addEventListener("hashchange", onChange);
    return () => window.removeEventListener("hashchange", onChange);
  }, []);

  const navigate = useCallback((view, param) => {
    window.location.hash = `/${view}${param ? `/${encodeURIComponent(param)}` : ""}`;
  }, []);

  return [route, navigate];
}
