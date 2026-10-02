import { defineConfig, loadEnv } from 'vite';
import react from '@vitejs/plugin-react';
import fs from 'node:fs';
import path from 'node:path';

// [AUTH Phase 2 — §4.2 HTTPS-dev MANDATORY] Browser↔frontend AND frontend→API all run over
// TLS in dev — NO HTTP fallback, NO "localhost exception" assumptions (playbook-approved).
// The Vite dev server reuses the ASP.NET development certificate (already trusted in the OS
// store via `dotnet dev-certs https --trust`) — exported as PEM and read from here. If the
// PEM files are missing, FAIL LOUDLY instead of silently downgrading to HTTP.
//
// [FIX-DEPLOY 2026-10-02] The requirement is DEV-SERVER-ONLY (`vite serve`). It used to run at
// config-load for every command, which made the PRODUCTION deployment impossible: the frontend
// image runs `npm run build` inside Docker where no dev certificate exists → the build threw and
// `docker compose up --build` failed. `vite build` emits a static bundle served by Nginx, so it
// must never require a dev cert.
// https://vite.dev/config/
export default defineConfig(({ command, mode }) => {
  const env = loadEnv(mode, process.cwd(), '');

  let httpsOptions: { key: Buffer; cert: Buffer } | undefined;
  if (command === 'serve') {
    const certDir = path.resolve(process.env.AUTH_DEV_CERTS ?? '../../certs-dev');
    const keyFile = path.join(certDir, 'localhost.key');
    const certFile = path.join(certDir, 'localhost.pem');
    if (!fs.existsSync(keyFile) || !fs.existsSync(certFile)) {
      throw new Error(
        `[AUTH §4.2] Dev HTTPS certificates not found in ${certDir}. ` +
        'Run: dotnet dev-certs https -ep <dir>/localhost.pem --format Pem --no-password ' +
        '(exports both .pem and .key). HTTP dev is NOT an approved fallback — see AUTH_MIGRATION_PLAYBOOK §4.2.');
    }
    httpsOptions = { key: fs.readFileSync(keyFile), cert: fs.readFileSync(certFile) };
  }

  // §4.2 Option A (same-origin): the browser ONLY talks to the Vite origin; the /api proxy
  // forwards to the backend's HTTPS endpoint (Aspire injects services__server__https__0).
  // NOTE: force 127.0.0.1 instead of localhost — Node resolves localhost to ::1 (IPv6) first
  // while the ASP.NET dev server binds IPv4 only → ECONNREFUSED proxy errors otherwise.
  // (User-approved 2026-09-06: proxy target 127.0.0.1; cert SAN implications checked — §4.2.2.)
  const backendUrl = env.VITE_API_BASE_URL
    || process.env.services__server__https__0
    || 'https://127.0.0.1:7314';

  return {
    plugins: [react()],
    server: {
      ...(httpsOptions ? { https: httpsOptions } : {}),
      proxy: {
        '/api': {
          target: backendUrl,
          changeOrigin: true,
          secure: true, // dev cert is trusted — keep TLS verification ON (was false for the old HTTP target)
        },
      },
    },
  };
});
