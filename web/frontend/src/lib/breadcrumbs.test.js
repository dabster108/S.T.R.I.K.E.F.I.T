import test from "node:test";
import assert from "node:assert/strict";
import { breadcrumbs, TITLES } from "./breadcrumbs.js";

test("the overview is one crumb", () => {
  assert.deepEqual(breadcrumbs("/dashboard"), [{ label: "Overview" }]);
  assert.deepEqual(breadcrumbs("/dashboard/"), [{ label: "Overview" }]);
});

test("a section is Dashboard > Section, the last crumb has no link", () => {
  assert.deepEqual(breadcrumbs("/dashboard/lab"), [{ label: "Dashboard", to: "/dashboard" }, { label: "Thesis Lab" }]);
});

test("detail pages sit under their list, with the name decoded", () => {
  assert.deepEqual(breadcrumbs("/dashboard/player/Ada%20Lovelace"), [
    { label: "Dashboard", to: "/dashboard" },
    { label: "Players", to: "/dashboard/players" },
    { label: "Ada Lovelace" },
  ]);
  const s = breadcrumbs("/dashboard/session/3f9a1c2e-1111-2222");
  assert.equal(s.at(-1).label, "Session 3f9a1c2e");
  assert.equal(s[1].to, "/dashboard/sessions");
});

test("an unknown page and a bad escape do not throw", () => {
  assert.equal(breadcrumbs("/dashboard/nope").at(-1).label, "Page not found");
  assert.equal(breadcrumbs("/dashboard/player/%E0%A4%A").at(-1).label, "%E0%A4%A");
});

test("every title is non-empty", () => {
  for (const v of Object.values(TITLES)) assert.ok(v.length > 0);
});
