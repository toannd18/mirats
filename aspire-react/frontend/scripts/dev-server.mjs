#!/usr/bin/env node
/**
 * [AUTH Phase 2 — §4.2 HTTPS-dev] Dev-server launcher.
 *
 * WHY this exists: the Vite dev server proxies /api → the backend's HTTPS endpoint
 * (https://127.0.0.1:7314). That endpoint uses the ASP.NET development certificate, which is
 * trusted by Windows/browser (Schannel) but NOT by Node — Node ships its own baked-in CA store
 * and a self-signed dev cert fails with DEPTH_ZERO_SELF_SIGNED_CERT (--use-system-ca does NOT
 * fix it on Windows because the ASP.NET cert is a self-signed LEAF placed directly in the Root
 * store, which Node rejects as a non-CA).
 *
 * SOLUTION (user-approved, Option 1 refined): point NODE_EXTRA_CA_CERTS at the SAME exported
 * ASP.NET cert PEM (certs-dev/localhost.pem — reused, no separate CA/cert infrastructure).
 * TLS verification stays FULLY ON (`secure: true` in vite.config.ts) — nothing is disabled.
 *
 * Preconditions (fail LOUDLY, never silently downgrade to HTTP — AUTH_MIGRATION_PLAYBOOK §4.2):
 *   1. Node >= 22.12 (per package.json engines)
 *   2. certs-dev/localhost.pem exists (dotnet dev-certs https -ep ... --format Pem --no-password)
 */
import fs from 'node:fs';
import path from 'node:path';
import { spawn } from 'node:child_process';
import { fileURLToPath } from 'node:url';

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const frontendDir = path.resolve(__dirname, '..');
const certFile = path.resolve(frontendDir, '../../certs-dev/localhost.pem');

function fail(message) {
  console.error(`\n[AUTH §4.2] ${message}\n`);
  process.exit(1);
}

// --- Preconditions ---
const [major, minor] = process.versions.node.split('.').map(Number);
if (major < 22 || (major === 22 && minor < 12)) {
  fail(`Node >= 22.12 is REQUIRED for HTTPS dev (found ${process.versions.node}). ` +
    'The dev stack trusts the ASP.NET dev cert via NODE_EXTRA_CA_CERTS, which needs Node 22+. ' +
    'HTTP dev is NOT an approved fallback — see AUTH_MIGRATION_PLAYBOOK §4.2.');
}

if (!fs.existsSync(certFile)) {
  fail(`Dev HTTPS certificate not found: ${certFile}\n` +
    'Run: dotnet dev-certs https -ep ' + certFile + ' --format Pem --no-password\n' +
    '(exports localhost.pem + localhost.key for BOTH Vite TLS and Node CA trust).');
}

// --- Launch Vite with the dev cert trusted by Node ---
process.env.NODE_EXTRA_CA_CERTS = certFile;

const vite = spawn(process.execPath, ['node_modules/vite/bin/vite.js'], {
  cwd: frontendDir,
  stdio: 'inherit',
  env: process.env,
});

vite.on('exit', (code, signal) => process.exit(code ?? (signal ? 1 : 0)));
vite.on('error', (err) => fail(`Failed to start Vite: ${err.message}`));

// Forward SIGINT/SIGTERM so Ctrl+C stops Vite cleanly.
for (const sig of ['SIGINT', 'SIGTERM']) {
  process.on(sig, () => vite.kill(sig));
}
