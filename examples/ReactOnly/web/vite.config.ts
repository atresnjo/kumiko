import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";

// The desktop host serves dist/ from memory at kumiko://localhost/, so the
// default absolute base works unchanged. strictPort keeps the dev server at the
// URL passed to --dev-server; the host only trusts that exact origin.
export default defineConfig({
  plugins: [react()],
  server: { port: 5173, strictPort: true },
  build: { target: "es2022" },
});
