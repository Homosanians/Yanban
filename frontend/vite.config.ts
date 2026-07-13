import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";

// Dev: proxy /api to the backend so the SPA is same-origin (cookies just work).
// Prod: nginx serves the built dist/ and reverse-proxies /api (frontend/nginx.conf).
//
// The SignalR hub goes through this same rule as /api/hubs/board — the rewrite strips /api,
// so the backend still sees /hubs/board and its query-string token check keeps working. Being
// same-origin in both environments is why the app needs no CORS at all (ADR-11).
export default defineConfig({
  plugins: [react()],
  server: {
    proxy: {
      "/api": {
        target: "http://localhost:5214",
        changeOrigin: true,
        // Covers the WebSocket upgrade too, not just the negotiate POST.
        ws: true,
        rewrite: (path) => path.replace(/^\/api/, ""),
      },
    },
  },
});
