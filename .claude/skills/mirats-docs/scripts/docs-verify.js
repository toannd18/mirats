#!/usr/bin/env node
/**
 * docs-verify — đối chiếu tài liệu docs/*.md với code thực tế.
 *
 * Trích từ mỗi file .md:
 *   [1] error_code  (SNAKE_CASE_UPPER, VD `INVALID_CREDENTIALS`)
 *   [2] endpoint    (`METHOD /path`, VD `POST /auth/login`, `POST {id}/confirm`)
 *   [3] biến môi trường (ASPNETCORE_…, DOTNET_…, MIRATS_…, `Parameters:x`)
 *   [4] link nội bộ (markdown `[text](rel/path.md#anchor)`)
 * rồi đối chiếu với code backend + filesystem, in danh sách KHÔNG KHỚP.
 *
 * ⚠️ CHỈ ĐỌC file (`fs.readFileSync(...,'utf8')`) + in ra stdout — KHÔNG ghi file.
 *   → không có rủi ro mojibake (tuân thủ quy tắc AGENTS.md "CẤM sửa file tiếng Việt
 *     qua PowerShell Get-Content/Set-Content").
 *
 * Usage:
 *   node docs-verify.js <file.md> [<file.md> ...]   # các file cụ thể
 *   node docs-verify.js --dir docs                  # quét toàn bộ *.md trong thư mục
 *   node docs-verify.js --code <dir> ...            # thêm dir code để đối chiếu
 *
 * Exit code: 0 = không có gì lệch; 1 = có mục không khớp (dùng được làm gate).
 */

'use strict';
const fs = require('fs');
const path = require('path');

// Repo root: scripts/ -> mirats-docs/ -> skills/ -> .claude/ -> root
const ROOT = path.resolve(__dirname, '..', '..', '..', '..');

const DEFAULT_CODE_DIRS = [
  'aspire-react/aspire-react.Application',
  'aspire-react/aspire-react.Domain',
  'aspire-react/aspire-react.Infrastructure',
  'aspire-react/aspire-react.Server',
  'aspire-react/aspire-react.AppHost',
  'aspire-react/frontend/src',
];

const CODE_EXTS = ['.cs', '.json', '.jsonc', '.csproj', '.env', '.ts', '.tsx', '.js', '.yml', '.yaml'];

// File cấu hình ở repo root (không nằm trong codeDirs) — vẫn là nguồn sự thật cho env.
const ROOT_FILES = ['docker-compose.yml', 'docker-compose.npm.yml', '.env.example'];

// Token placeholder trong ví dụ JSON của docs — không phải error_code thật.
const ERROR_CODE_DENYLIST = new Set(['SOME_CODE', 'EXAMPLE_CODE', 'ERROR_CODE', 'XXXX']);

function listFiles(dir, exts, out = []) {
  let entries;
  try { entries = fs.readdirSync(dir, { withFileTypes: true }); } catch { return out; }
  for (const e of entries) {
    if (e.name === 'node_modules' || e.name === 'bin' || e.name === 'obj' || e.name === '.git') continue;
    const p = path.join(dir, e.name);
    if (e.isDirectory()) listFiles(p, exts, out);
    else if (exts.some((ext) => e.name.endsWith(ext))) out.push(p);
  }
  return out;
}

function read(p) { try { return fs.readFileSync(p, 'utf8'); } catch { return null; } }

// ---- regex trích xuất ----
const RE_ERROR_CODE = /\b[A-Z][A-Z0-9]{2,}_[A-Z0-9_]+\b/g;
const RE_ENDPOINT = /\b(GET|POST|PUT|PATCH|DELETE)\s+([A-Za-z0-9_{}:.,\-\/]+)/gi;
// Env: SNAKE_UPPER (prefix mở rộng) + dạng Section__Key (config .NET: Proxy__KnownNetworks).
const RE_ENV = /\b(?:ASPNETCORE|DOTNET|MIRATS|CONNECTIONSTRINGS|POSTGRES|PG|REDIS|REDISCLI|OAUTH|AUTH|VITE|PROXY|NPM|WEBAUTHN|PGADMIN|INITIAL|CORS|BACKEND|FRONTEND)_[A-Z0-9_]{1,}[A-Z0-9]\b|\b[A-Za-z][A-Za-z0-9]*__[A-Za-z][A-Za-z0-9_]*\b|Parameters:[A-Za-z0-9]+/g;
const RE_LINK = /\[[^\]]*\]\(([^)\s]+)\)/g;

// Chuẩn hoá env để so sánh: tách thành các từ (gạch dưới + biên camelCase),
// hoa hết, nối bằng gạch dưới đơn. Nhờ đó PROXY_KNOWN_NETWORKS (compose/.env)
// và Proxy__KnownNetworks (container .NET) được coi là một.
function normEnv(t) {
  return String(t)
    .split(/__+/)
    .flatMap((p) => p.split(/(?=[A-Z][a-z])|_+/))
    .filter((w) => w.length > 0)
    .map((w) => w.toUpperCase())
    .join('_');
}

// Chuẩn hoá route path: lowercase, bỏ prefix api/v1, placeholder -> '*'
function normalizeRoute(p) {
  let s = String(p).toLowerCase().trim();
  s = s.replace(/^\/+|\/+$/g, '');
  s = s.replace(/^api\/v\d+\//, '');
  s = s.replace(/\{[^}]*\}/g, '*');
  s = s.replace(/\/+/g, '/');
  return s;
}

// Bỏ dấu câu đuôi + slash thừa trên path trích từ docs.
function cleanPath(p) {
  return String(p).replace(/[,.;|]+$/g, '').replace(/^\/+|\/+$/g, '');
}

// Mở rộng ký hiệu shorthand `{a,b,c}` trong path docs -> danh sách path.
// Chỉ mở rộng khi có dấu phẩy (liệt kê thật); `{param}` đơn là placeholder
// route -> giữ nguyên để normalizeRoute đổi thành `*`.
function expandBraces(p) {
  const m = p.match(/\{([^{}]+)\}/);
  if (!m) return [p];
  if (!m[1].includes(',')) return [p];
  const opts = m[1].split(',');
  return opts.flatMap((o) => expandBraces(p.slice(0, m.index) + o + p.slice(m.index + m[0].length)));
}

// ---- index code ----
function buildTokenIndex(codeDirs, re, norm) {
  const files = [];
  for (const d of codeDirs) listFiles(path.join(ROOT, d), CODE_EXTS, files);
  for (const f of ROOT_FILES) files.push(path.join(ROOT, f));
  const set = new Set();
  for (const f of files) {
    const text = read(f);
    if (!text) continue;
    const m = text.match(re);
    if (m) for (const t of m) set.add(norm ? norm(t) : t);
  }
  return set;
}

function buildRouteIndex() {
  const controllerDir = path.join(ROOT, 'aspire-react/aspire-react.Server/Web/Controllers');
  const files = listFiles(controllerDir, ['.cs']);
  const routes = new Set(); // 'METHOD /normalized'
  const RE_METHOD = /\[Http(Get|Post|Put|Delete|Patch)(?:\("([^"]*)"\))?[^\\\]]*\]/g;
  for (const f of files) {
    const text = read(f);
    if (!text) continue;
    const clsRouteMatch = text.match(/Route\("([^"]+)"\)/);
    let classRoute = clsRouteMatch ? clsRouteMatch[1] : '';
    const clsName = (text.match(/class\s+(\w+)Controller/) || [])[1] || '';
    classRoute = classRoute.replace('[controller]', clsName);
    classRoute = normalizeRoute(classRoute);

    let m;
    RE_METHOD.lastIndex = 0;
    while ((m = RE_METHOD.exec(text)) !== null) {
      const method = m[1].toUpperCase();
      const tmpl = m[2] || '';
      const full = normalizeRoute((classRoute + '/' + tmpl).replace(/\/+/g, '/'));
      routes.add(method + ' ' + full);
    }
  }
  return routes;
}

// So khớp endpoint theo SUFFIX (endpoint viết tắt kiểu "{id}/confirm" khớp với
// route đầy đủ "assets/{id}/confirm").
function segmentMatch(routeSegs, epSegs) {
  if (routeSegs.length < epSegs.length) return false;
  const offset = routeSegs.length - epSegs.length;
  for (let i = 0; i < epSegs.length; i++) {
    const r = routeSegs[offset + i];
    const e = epSegs[i];
    if (r === '*' || e === '*') continue;
    if (r !== e) return false;
  }
  return true;
}

function endpointMatches(routes, method, epPath) {
  const epSegs = normalizeRoute(epPath).split('/').filter(Boolean);
  if (epSegs.length === 0) return true; // không đủ thông tin -> bỏ qua
  if (suffixMatchAny(routes, method, epSegs)) return true;
  // shorthand dạng danh sách từ khóa (không placeholder): `close/inspect/reopen`
  // -> thử từng "lá" như một suffix ĐÚNG LITERAL (không để placeholder `*` khớp bừa).
  if (epSegs.every((s) => /^[a-z][a-z0-9-]*$/.test(s))) {
    return epSegs.every((s) => leafIsSuffix(routes, method, s));
  }
  return false;
}

function leafIsSuffix(routes, method, leaf) {
  for (const r of routes) {
    const sp = r.indexOf(' ');
    if (r.slice(0, sp) !== method) continue;
    const segs = r.slice(sp + 1).split('/').filter(Boolean);
    if (segs.length && segs[segs.length - 1] === leaf) return true;
  }
  return false;
}

function suffixMatchAny(routes, method, segs) {
  for (const r of routes) {
    const sp = r.indexOf(' ');
    if (r.slice(0, sp) !== method) continue;
    const rSegs = r.slice(sp + 1).split('/').filter(Boolean);
    if (segmentMatch(rSegs, segs)) return true;
  }
  return false;
}

// ---- main ----
function main() {
  const args = process.argv.slice(2);
  const docFiles = [];
  const codeDirs = DEFAULT_CODE_DIRS.slice();
  let dir = null;

  for (let i = 0; i < args.length; i++) {
    const a = args[i];
    if (a === '--dir') dir = args[++i];
    else if (a === '--code') codeDirs.push(args[++i]);
    else docFiles.push(a);
  }

  let files;
  if (dir) files = listFiles(path.resolve(ROOT, dir), ['.md']);
  else files = docFiles;
  files = files.map((f) => path.resolve(f));

  if (files.length === 0) {
    console.error('Usage: node docs-verify.js [file.md ...] | --dir <docsdir> [--code <dir>]');
    process.exit(2);
  }

  const errIdx = buildTokenIndex(codeDirs, RE_ERROR_CODE);
  const envIdx = buildTokenIndex(codeDirs, RE_ENV, normEnv);
  const routeIdx = buildRouteIndex();

  let nErr = 0, nEp = 0, nEnv = 0, nLink = 0;
  const missErr = [], missEp = [], missEnv = [], broken = [];
  const seenErr = new Set(), seenEp = new Set(), seenEnv = new Set(), seenLink = new Set();

  for (const f of files) {
    let text = read(f);
    if (text == null) { console.error('Không đọc được: ' + f); continue; }
    // Bỏ khối ```mermaid: nhãn sơ đồ (tên bảng/entity VIẾT HOA) không phải
    // error_code/endpoint/env — sơ đồ kiểm chứng bằng mermaid parse + review.
    text = text.replace(/```mermaid\r?\n[\s\S]*?```/g, '');
    const rel = path.relative(ROOT, f);
    let m;

    RE_ERROR_CODE.lastIndex = 0;
    while ((m = RE_ERROR_CODE.exec(text)) !== null) {
      const t = m[0];
      if (ERROR_CODE_DENYLIST.has(t) || seenErr.has(t)) continue;
      // Bỏ tên file tài liệu (VD DEVELOPMENT_WORKFLOW.md) — không phải error_code.
      const after = text.slice(m.index + t.length, m.index + t.length + 4);
      if (after.toLowerCase().startsWith('.md')) continue;
      seenErr.add(t); nErr++;
      if (!errIdx.has(t)) missErr.push(t + '   (' + rel + ')');
    }

    RE_ENDPOINT.lastIndex = 0;
    while ((m = RE_ENDPOINT.exec(text)) !== null) {
      const method = m[1].toUpperCase();
      const rawPath = cleanPath(m[2]);
      if (!rawPath) continue;
      const paths = expandBraces(rawPath);
      for (const p of paths) {
        const key = method + ' ' + p;
        if (seenEp.has(key)) continue;
        seenEp.add(key); nEp++;
        if (!endpointMatches(routeIdx, method, p)) missEp.push(key + '   (' + rel + ')');
      }
    }

    RE_ENV.lastIndex = 0;
    while ((m = RE_ENV.exec(text)) !== null) {
      const t = m[0];
      if (seenEnv.has(t)) continue;
      seenEnv.add(t); nEnv++;
      if (!envIdx.has(normEnv(t))) missEnv.push(t + '   (' + rel + ')');
    }

    RE_LINK.lastIndex = 0;
    while ((m = RE_LINK.exec(text)) !== null) {
      const href = m[1];
      if (/^(https?:|mailto:|#|data:)/i.test(href)) continue;
      const target = href.split('#')[0];
      if (!target) continue;
      const key = rel + ' -> ' + href;
      if (seenLink.has(key)) continue;
      seenLink.add(key); nLink++;
      if (!fs.existsSync(path.resolve(path.dirname(f), target))) broken.push(rel + '  ->  ' + href);
    }
  }

  console.log('===== docs-verify =====');
  console.log('Docs : ' + files.length + ' file(s)');
  console.log('Code : ' + codeDirs.join(', '));
  console.log('');

  const report = (title, n, miss) => {
    console.log('[' + title + ']  trích ' + n + ', không khớp ' + miss.length);
    for (const x of miss) console.log('  ✗ ' + x);
    if (miss.length === 0) console.log('  ✓ OK');
    console.log('');
  };

  report('1. Error codes (vs code)', nErr, missErr);
  report('2. Endpoints (vs controllers)', nEp, missEp);
  report('3. Environment variables (vs code)', nEnv, missEnv);
  report('4. Internal links (vs filesystem)', nLink, broken);

  const totalMiss = missErr.length + missEp.length + missEnv.length + broken.length;
  console.log(totalMiss === 0
    ? 'KẾT QUẢ: PASS'
    : 'KẾT QUẢ: FAIL — ' + totalMiss + ' mục không khớp (xem chi tiết trên)');
  process.exit(totalMiss === 0 ? 0 : 1);
}

// ---- self-test: kiểm tra parser trên chuỗi mẫu (không cần repo) ----
function selfTest() {
  let pass = 0, fail = 0;
  const eq = (name, got, want) => {
    const ok = JSON.stringify(got) === JSON.stringify(want);
    if (ok) { pass++; console.log('  ok: ' + name); }
    else { fail++; console.log('  FAIL: ' + name + '\n    got:  ' + JSON.stringify(got) + '\n    want: ' + JSON.stringify(want)); }
  };
  const envs = (s) => {
    RE_ENV.lastIndex = 0;
    const out = [];
    let m;
    while ((m = RE_ENV.exec(s)) !== null) out.push(m[0]);
    return out;
  };

  // [1] SNAKE các prefix + Section__Key
  eq('env snake', envs('dùng POSTGRES_PASSWORD và REDIS_PORT, AUTH_SIGNING_KEY.'), ['POSTGRES_PASSWORD', 'REDIS_PORT', 'AUTH_SIGNING_KEY']);
  eq('env proxy/npm/webauthn', envs('PROXY_KNOWN_NETWORKS, NPM_NETWORK_NAME, WEBAUTHN_RPID, PROXY_FORWARD_LIMIT.'), ['PROXY_KNOWN_NETWORKS', 'NPM_NETWORK_NAME', 'WEBAUTHN_RPID', 'PROXY_FORWARD_LIMIT']);
  eq('env Section__Key', envs('cấu hình Proxy__KnownNetworks và Auth__WebAuthn__RPId.'), ['Proxy__KnownNetworks', 'Auth__WebAuthn__RPId']);
  eq('env trong bảng', envs('| `VITE_API_BASE_URL` | `/api/v1` |'), ['VITE_API_BASE_URL']);
  eq('env trong code', envs('```\nPOSTGRES_DB=aspire-react-db\n```'), ['POSTGRES_DB']);
  // [2] chuẩn hoá so khớp compose vs container
  eq('normEnv', [normEnv('Proxy__KnownNetworks'), normEnv('PROXY_KNOWN_NETWORKS'), normEnv('Auth__SigningKey'), normEnv('AUTH_SIGNING_KEY')], ['PROXY_KNOWN_NETWORKS', 'PROXY_KNOWN_NETWORKS', 'AUTH_SIGNING_KEY', 'AUTH_SIGNING_KEY']);
  // [3] không bắt chữ thường / câu văn
  eq('env bỏ qua text thường', envs('mật khẩu và chữ ký bình thường.'), []);
  eq('env bỏ qua số phiên bản', envs('bản 10.0.10 và Redis 8.'), []);

  console.log(fail === 0 ? `SELF-TEST: PASS (${pass})` : `SELF-TEST: FAIL (${fail}/${pass + fail})`);
  process.exit(fail === 0 ? 0 : 1);
}

if (process.argv.includes('--self-test')) selfTest();
else main();
