#!/usr/bin/env node
/**
 * install-skills — tái lập các skill KHÔNG track trên máy mới.
 *
 * Đọc manifest "Reinstall manifest" trong `.claude/skills/SOURCES.md`
 * (bảng: skill | repo | path | commit), sparse-clone đúng commit từ GitHub,
 * copy vào `.claude/skills/<skill>/`. Không phân phối lại nội dung qua git.
 *
 * ⚠️ CHỈ ĐỌC SOURCES.md + clone mạng + copy file mới vào skill còn thiếu.
 *   Không ghi/sửa file có sẵn (trừ copy khi thư mục đích chưa tồn tại).
 *
 * Usage:
 *   node scripts/install-skills.mjs                 # cài mọi skill trong manifest còn thiếu
 *   node scripts/install-skills.mjs --force         # ghi đè cả skill đã có
 *   node scripts/install-skills.mjs --list          # chỉ liệt kê manifest
 */

import fs from 'node:fs';
import path from 'node:path';
import os from 'node:os';
import { execSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';

const ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const SOURCES = path.join(ROOT, '.claude', 'skills', 'SOURCES.md');
const DEST = path.join(ROOT, '.claude', 'skills');

function parseManifest() {
  const text = fs.readFileSync(SOURCES, 'utf8');
  const lines = text.split(/\r?\n/);
  const rows = [];
  let inManifest = false;
  for (const line of lines) {
    if (/^##\s+Reinstall manifest/.test(line)) { inManifest = true; continue; }
    if (inManifest && /^##\s+/.test(line)) break;
    if (!inManifest) continue;
    const m = line.match(/^\|\s*([^|]+?)\s*\|\s*([^|]+?)\s*\|\s*([^|]+?)\s*\|\s*([0-9a-f]{7,40})\s*\|$/i);
    if (!m || /^\s*skill\s*$/i.test(m[1])) continue;
    rows.push({ skill: m[1].trim(), repo: m[2].trim(), skillPath: m[3].trim(), commit: m[4].trim() });
  }
  return rows;
}

function sh(cmd, cwd) {
  execSync(cmd, { cwd, stdio: 'inherit', shell: true });
}

function copyDir(src, dst) {
  fs.mkdirSync(dst, { recursive: true });
  for (const e of fs.readdirSync(src, { withFileTypes: true })) {
    const s = path.join(src, e.name);
    const d = path.join(dst, e.name);
    if (e.isDirectory()) copyDir(s, d);
    else if (e.name !== '.git') fs.copyFileSync(s, d);
  }
}

function main() {
  const args = process.argv.slice(2);
  const force = args.includes('--force');
  const listOnly = args.includes('--list');
  const rows = parseManifest();

  if (rows.length === 0) {
    console.error('Không tìm thấy manifest trong SOURCES.md');
    process.exit(2);
  }

  console.log('Manifest (' + rows.length + ' skills):');
  for (const r of rows) console.log('  - ' + r.skill + '  <=  ' + r.repo + ' @ ' + r.commit + '  [' + r.skillPath + ']');
  if (listOnly) return;

  const tmp = fs.mkdtempSync(path.join(os.tmpdir(), 'install-skills-'));
  try {
    for (const r of rows) {
      const destDir = path.join(DEST, r.skill);
      if (fs.existsSync(destDir) && !force) {
        console.log('SKIP (đã có): ' + r.skill);
        continue;
      }
      const cloneDir = path.join(tmp, r.skill);
      console.log('Clone ' + r.repo + ' @ ' + r.commit + ' ...');
      sh('git clone --filter=blob:none --no-checkout "https://github.com/' + r.repo + '.git" "' + cloneDir + '"');
      sh('git sparse-checkout set "' + r.skillPath + '"', cloneDir);
      sh('git checkout ' + r.commit, cloneDir);
      copyDir(path.join(cloneDir, r.skillPath), destDir);
      console.log('OK: ' + r.skill + ' -> ' + path.relative(ROOT, destDir));
    }
  } finally {
    fs.rmSync(tmp, { recursive: true, force: true });
  }
  console.log('Xong. Kiểm tra: git status --porcelain -- .claude/skills/');
}

main();
