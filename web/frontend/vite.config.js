import { defineConfig, loadEnv } from "vite";
import react from "@vitejs/plugin-react";
import tailwindcss from "@tailwindcss/vite";

export default defineConfig(({ mode }) => {
  // Dev proxy target; override with BACKEND_URL=http://host:port npm run dev
  const env = loadEnv(mode, process.cwd(), "");
  const backend = env.BACKEND_URL || "http://localhost:4000";

  return {
    plugins: [react(), tailwindcss()],
    server: {
      port: 5173,
      host: true,
      proxy: {
        "/api": backend,
        "/socket.io": { target: backend, ws: true },
      },
    },
  };
});
