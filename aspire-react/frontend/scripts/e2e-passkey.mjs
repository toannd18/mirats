#!/usr/bin/env node
/**
 * [AUTH Phase 3] E2E passkey verification with the Chromium VIRTUAL AUTHENTICATOR (CDP).
 *
 * Flow (all sequential, single browser, ~bounded per step):
 *  1. Admin API: create fixture user + reset-password (mustChange=true) + enable passkey flag
 *  2. Browser: password login (forced change) → change password → dashboard
 *  3. /account → register passkey via navigator.credentials.create (virtual authenticator)
 *  4. logout → /login shows passkey button → username-less passkey login → dashboard
 *  5. Flag OFF → button hidden + POST login/options → 403 PASSKEYS_DISABLED
 *  6. Cleanup: delete fixture user (soft-delete via API), flag left OFF (default)
 *
 * Env: ADMIN_TOKEN (Keycloak bearer), FIXTURE_NEW_PASS (optional, default provided)
 */
import { createRequire } from 'node:module';
import { execSync } from 'node:child_process';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const BASE = process.env.BASE_URL ?? 'https://localhost:5173';
const ADMIN_TOKEN = process.env.ADMIN_TOKEN ?? (() => { fail('ADMIN_TOKEN env required'); })();
const NEW_PASS = process.env.FIXTURE_NEW_PASS ?? 'QaPass#Phase3e2e';
const TS = Date.now().toString(36);
const FIXTURE_USER = `qa-phase3-passkey-${TS}`;

// Node reads NODE_EXTRA_CA_CERTS ONLY at startup — the INVOKER must set it (PowerShell:
//   $env:NODE_EXTRA_CA_CERTS = "<repo>/certs-dev/localhost.pem" before `node scripts/e2e-passkey.mjs`).
const certFile = path.resolve(__dirname, '../../../certs-dev/localhost.pem');
if (!fs.existsSync(certFile)) fail(`dev cert missing: ${certFile}`);
if (!process.env.NODE_EXTRA_CA_CERTS) fail('NODE_EXTRA_CA_CERTS not set — invoke via PowerShell with $env:NODE_EXTRA_CA_CERTS = <repo>/certs-dev/localhost.pem');

function fail(message) {
  console.error(`\n[E2E-PASSKEY-FAIL] ${message}\n`);
  process.exit(1);
}

async function api(method, urlPath, body, useAdmin = true) {
  const res = await fetch(`${BASE}${urlPath}`, {
    method,
    headers: {
      'Content-Type': 'application/json',
      ...(useAdmin ? { Authorization: `Bearer ${ADMIN_TOKEN}` } : {})
    },
    body: body === undefined ? undefined : JSON.stringify(body)
  });
  let json = null;
  try { json = await res.json(); } catch { /* raw endpoints */ }
  return { status: res.status, json };
}

const results = [];
function record(step, ok, detail = '') {
  results.push({ step, ok, detail });
  console.log(`${ok ? 'PASS' : 'FAIL'} | ${step}${detail ? ' | ' + detail : ''}`);
  if (!ok) fail(`step failed: ${step} ${detail}`);
}

// ---------- resolve playwright-core from the global playwright-cli install ----------
const globalRoot = execSync('npm root -g', { encoding: 'utf8' }).trim();
const require = createRequire(import.meta.url);
const pwCore = require(path.join(globalRoot, '@playwright/cli/node_modules/playwright-core'));

// ---------- 1. Admin setup ----------
// [AUTH Phase 4] CreateUser now REQUIRES an initial password (local-only creation).
const created = await api('POST', '/api/v1/users', {
  username: FIXTURE_USER, email: `${FIXTURE_USER}@test.local`, firstName: 'QA', lastName: 'Passkey',
  password: 'QaTemp#Phase3x1'
});
record('create fixture user', created.status === 201, `user=${FIXTURE_USER}`);
const userId = created.json?.data?.id;
const reset = await api('POST', `/api/v1/users/${userId}/reset-password`, { newPassword: 'QaTemp#Phase3x1' });
record('reset-password (mustChange=true)', reset.status === 200);
const enable = await api('PUT', '/api/v1/system/config/passkeys-enabled', { enabled: true });
record('enable passkey flag', enable.status === 200);

// ---------- 2. Browser with virtual authenticator ----------
const browser = await pwCore.chromium.launch({ headless: true, ignoreHTTPSErrors: true }).catch(() =>
  pwCore.chromium.launch({ headless: true, ignoreHTTPSErrors: true, channel: 'chrome' }));
try {
  const context = await browser.newContext({ viewport: { width: 1440, height: 900 } });
  const page = await context.newPage();
  // diagnostics captured for failure dumps
  const consoleErrors = [];
  page.on('console', m => { if (m.type() === 'error') consoleErrors.push(m.text().substring(0, 200)); });
  const apiCalls = [];
  page.on('response', async r => {
    const u = r.url();
    if (u.includes('/api/v1/auth/passkeys') || u.includes('/api/v1/auth/refresh')) {
      let body = '';
      try { body = (await r.text()).substring(0, 220); } catch { /* body gone */ }
      apiCalls.push(`${r.status()} ${r.request().method()} ${new URL(u).pathname} auth=${r.request().headers()['authorization'] ? 'yes' : 'NO'} body=${body}`);
    }
  });
  const cdp = await context.newCDPSession(page);
  await cdp.send('WebAuthn.enable');
  await cdp.send('WebAuthn.addVirtualAuthenticator', {
    options: {
      protocol: 'ctap2', transport: 'internal', hasResidentKey: true,
      hasUserVerification: true, isUserVerified: true, automaticPresenceSimulation: true
    }
  });

  // --- password login (forced change flow) ---
  await page.goto(`${BASE}/login`, { waitUntil: 'domcontentloaded', timeout: 60000 });
  await page.fill('#username', FIXTURE_USER);
  await page.fill('#password', 'QaTemp#Phase3x1');
  await page.getByRole('button', { name: 'Đăng nhập', exact: true }).click();
  await page.waitForURL('**/change-password', { timeout: 30000 });
  record('forced change-password redirect', true);

  await page.fill('#currentPassword', 'QaTemp#Phase3x1');
  await page.fill('#newPassword', NEW_PASS);
  await page.fill('#confirm', NEW_PASS);
  await page.getByRole('button', { name: /Đổi mật khẩu/ }).click();
  await page.waitForURL('**/login', { timeout: 30000 });
  record('password changed → back to login', true);

  await page.fill('#username', FIXTURE_USER);
  await page.fill('#password', NEW_PASS);
  await page.getByRole('button', { name: 'Đăng nhập', exact: true }).click();
  await page.waitForURL('**/dashboard', { timeout: 30000 });
  record('re-login with new password → dashboard', true);

  // --- 3. register passkey on /account ---
  await page.goto(`${BASE}/account`, { waitUntil: 'domcontentloaded', timeout: 60000 });
  try {
    await page.fill('input[placeholder*="Tên passkey"]', 'E2E Virtual Key', { timeout: 15000 });
  } catch (err) {
    const dump = await page.evaluate(() => ({
      url: window.location.pathname,
      body: document.body.innerText.substring(0, 400)
    })).catch(() => ({ url: 'eval-failed', body: '' }));
    fail(`account input not found. URL=${dump.url} BODY=${dump.body}`);
  }
  await page.getByRole('button', { name: /Đăng ký Passkey/ }).click();
  try {
    await page.locator('.ant-list-item', { hasText: 'E2E Virtual Key' }).waitFor({ timeout: 30000 });
  } catch (err) {
    const dump = await page.evaluate(() => ({
      url: window.location.pathname,
      body: document.body.innerText.substring(0, 500)
    })).catch(() => ({ url: 'eval-failed', body: '' }));
    fail(`passkey list item not visible. URL=${dump.url} BODY=${dump.body} API=[${apiCalls.join(' | ')}] CONSOLE=[${consoleErrors.join(' || ')}]`);
  }
  record('passkey registered + listed on /account', true);

  // screenshots at 3 breakpoints (sequential, same page)
  for (const [w, h] of [[1440, 900], [768, 1024], [375, 812]]) {
    await page.setViewportSize({ width: w, height: h });
    await page.waitForTimeout(300);
    await page.screenshot({ path: `D:/Person/Applications/Aspire Project/phase3-account-${w}.png` });
  }
  await page.setViewportSize({ width: 1440, height: 900 });
  record('account screenshots at 375/768/1440', true);

  // --- 4. logout → passkey login ---
  await page.evaluate(async () => {
    await fetch('/api/v1/auth/logout', { method: 'POST', credentials: 'include' });
  });
  await page.goto(`${BASE}/login`, { waitUntil: 'domcontentloaded', timeout: 60000 });
  const passkeyBtn = page.getByRole('button', { name: /Đăng nhập bằng Passkey/ });
  await passkeyBtn.waitFor({ timeout: 15000 });
  record('flag ON → passkey button visible on /login', true);
  await passkeyBtn.click();
  await page.waitForURL('**/dashboard', { timeout: 30000 });
  record('passkey login (virtual authenticator) → dashboard', true);

  // --- 5. flag OFF ---
  const disable = await api('PUT', '/api/v1/system/config/passkeys-enabled', { enabled: false });
  record('disable passkey flag', disable.status === 200);
  await page.goto(`${BASE}/login`, { waitUntil: 'domcontentloaded', timeout: 60000 });
  await page.waitForTimeout(800); // status probe settles
  const btnCount = await page.getByRole('button', { name: /Đăng nhập bằng Passkey/ }).count();
  record('flag OFF → passkey button hidden', btnCount === 0, `count=${btnCount}`);
  const blocked = await page.evaluate(async () => {
    const res = await fetch('/api/v1/auth/passkeys/login/options', {
      method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ username: null })
    });
    const body = await res.json().catch(() => null);
    return { status: res.status, code: body?.error_code };
  });
  record('flag OFF → login/options 403 PASSKEYS_DISABLED',
    blocked.status === 403 && blocked.code === 'PASSKEYS_DISABLED', JSON.stringify(blocked));
} finally {
  await browser.close().catch(() => {});
}

// ---------- 6. cleanup ----------
const deleted = await api('DELETE', `/api/v1/users/${userId}`);
record('cleanup fixture user', deleted.status === 200);
const flagState = await api('GET', '/api/v1/system/config/passkeys-enabled');
record('flag left OFF (default)', flagState.json?.data?.enabled === false);

console.log('\nE2E-PASSKEY: ALL STEPS PASSED');
