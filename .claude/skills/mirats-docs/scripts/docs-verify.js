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
const RE_ENV = /\b(?:ASPNETCORE|DOTNET|MIRATS|ConnectionStrings|POSTGRES|PG|REDIS|OAUTH|AUTH|VITE)_[A-Z0-9_]{2,}\b|Parameters:[A-Za-z0-9]+/g;
const RE_LINK = /\[[^\]]*\]\(([^)\s]+)\)/g;

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
function buildTokenIndex(codeDirs, re) {
  const files = [];
  for (const d of codeDirs) listFiles(path.join(ROOT, d), CODE_EXTS, files);
  for (const f of ROOT_FILES) files.push(path.join(ROOT, f));
  const set = new Set();
  for (const f of files) {
    const text = read(f);
    if (!text) continue;
    const m = text.match(re);
    if (m) for (const t of m) set.add(t);
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
  const envIdx = buildTokenIndex(codeDirs, RE_ENV);
  const routeIdx = buildRouteIndex();

  let nErr = 0, nEp = 0, nEnv = 0, nLink = 0;
  const missErr = [], missEp = [], missEnv = [], broken = [];
  const seenErr = new Set(), seenEp = new Set(), seenEnv = new Set(), seenLink = new Set();

  for (const f of files) {
    const text = read(f);
    if (text == null) { console.error('Không đọc được: ' + f); continue; }
    const rel = path.relative(ROOT, f);
    let m;

    RE_ERROR_CODE.lastIndex = 0;
    while ((m = RE_ERROR_CODE.exec(text)) !== null) {
      const t = m[0];
      if (ERROR_CODE_DENYLIST.has(t) || seenErr.has(t)) continue;
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
      if (!envIdx.has(t)) missEnv.push(t + '   (' + rel + ')');
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

main();
