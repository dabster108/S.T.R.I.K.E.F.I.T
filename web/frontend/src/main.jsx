import React from "react";
import { createRoot } from "react-dom/client";
import { BrowserRouter } from "react-router-dom";
import App from "./App.jsx";
import "./styles.css";
import { startSpotlight } from "./motion/spotlight.js";
import { initDemoFlag } from "./demo/mode.js";

startSpotlight(); // one delegated pointer listener for every [data-spotlight] card (pointer devices only)

// Demo mode (?demo=1 or the footer button) answers every /api request from recorded data and plays scripted fights on the live stream,
// so the site runs with no backend and no internet. Its code is only downloaded when it is on.
async function boot() {
  if (initDemoFlag()) await (await import("./demo/install.js")).installDemo();
  createRoot(document.getElementById("root")).render(
    <React.StrictMode>
      <BrowserRouter future={{ v7_startTransition: true, v7_relativeSplatPath: true }}>
        <App />
      </BrowserRouter>
    </React.StrictMode>
  );
}
boot();
