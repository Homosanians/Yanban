import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";

// Dev: proxy /api to the backend so the SPA is same-origin (cookies just work).
// Prod: nginx serves the built dist/ and reverse-proxies /api (frontend/nginx.conf).
export default defineConfig({
  plugins: [react()],
  server: {
    proxy: {
      "/api": {
        target: "http://localhost:5214",
        changeOrigin: true,
        rewrite: (path) => path.replace(/^\/api/, ""),
      },
    },
  },
});
