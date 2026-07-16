import { defineConfig, loadEnv } from "vite";
import react from "@vitejs/plugin-react";

// Dev: proxy /api to the backend so the SPA is same-origin (cookies just work).
// Prod: nginx serves the built dist/ and reverse-proxies /api (frontend/nginx.conf).
//
// The SignalR hub goes through this same /api rule. The rewrite strips /api, so the backend
// still sees /hubs/board and its query-string token check keeps working. Being same-origin in
// both environments is why the app needs no CORS.
//
// The API target is configurable via VITE_API_PROXY_TARGET (see .env.example), so running the
// API on a non-default port needs no edit here. It only affects `npm run dev`; the built SPA
// always calls the relative /api behind nginx.
export default defineConfig(({ mode }) => {
  const env = loadEnv(mode, process.cwd(), "");
  const apiTarget = env.VITE_API_PROXY_TARGET || "http://localhost:5214";

  return {
    plugins: [react()],
    server: {
      proxy: {
        "/api": {
          target: apiTarget,
          changeOrigin: true,
          // Covers the WebSocket upgrade too, not just the negotiate POST.
          ws: true,
          rewrite: (path) => path.replace(/^\/api/, ""),
        },
      },
    },
  };
});
