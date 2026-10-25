#!/usr/bin/env node
/**
 * Idempotent demo fixture for the Mirats UI review.
 * All records are created through the real HTTP API (never SQL/direct EF).
 *
 * Usage (from repo root):
 *   DEMO_API_BASE=http://127.0.0.1:5442/api/v1 \
 *   DEMO_ADMIN_PASSWORD='<admin-password>' \
 *   node scripts/seed-demo.mjs
 *
 * The target database must be a disposable one (mirats_tmp_demo). The script is safe to
 * re-run: it looks records up by their stable code/name/tag before creating.
 */
import fs from 'node:fs';
import path from 'node:path';

const BASE = (process.env.DEMO_API_BASE || 'http://127.0.0.1:5442/api/v1').replace(/\/$/, '');

// [DF-5] Never let demo fixtures touch a non-local environment by accident. Remote requires BOTH
// an explicit --allow-remote flag and DEMO_SEED_CONFIRM=yes (two independent confirmations).
const LOCAL_HOSTS = new Set(['localhost', '127.0.0.1', '::1', '[::1]']);
let seedHost = '';
try {
  seedHost = new URL(BASE).hostname.toLowerCase();
} catch {
  console.error(`[seed-demo] DEMO_API_BASE is not a valid URL: ${BASE}`);
  process.exit(1);
}
const allowRemote = process.argv.includes('--allow-remote');
if (!LOCAL_HOSTS.has(seedHost)) {
  if (!allowRemote || process.env.DEMO_SEED_CONFIRM !== 'yes') {
    console.error(
      `[seed-demo] Refusing to seed non-local API base "${BASE}".\n` +
      'This script creates demo companies, users and assets. To target a remote environment,\n' +
      'pass --allow-remote AND set DEMO_SEED_CONFIRM=yes. Never run it against production.',
    );
    process.exit(1);
  }
  console.warn(`[seed-demo] WARNING: seeding REMOTE environment ${BASE} (explicitly confirmed).`);
}

// [DF-5] Admin password must come from the environment — no hard-coded fallback. Demo user
// passwords may be overridden when a team shares a fixture environment.
const ADMIN_PASSWORD = process.env.DEMO_ADMIN_PASSWORD;
if (!ADMIN_PASSWORD) {
  console.error('[seed-demo] DEMO_ADMIN_PASSWORD is required (there is no default password).');
  process.exit(1);
}
// [DF-5b] Remote runs must not inherit the demo defaults for shared demo accounts.
if (allowRemote && (!process.env.DEMO_TECH_PASSWORD || !process.env.DEMO_LEADER_PASSWORD || !process.env.DEMO_LEAD_PASSWORD)) {
  console.error(
    '[seed-demo] --allow-remote requires DEMO_TECH_PASSWORD, DEMO_LEADER_PASSWORD and DEMO_LEAD_PASSWORD ' +
    '(demo account defaults are not allowed on a remote environment).',
  );
  process.exit(1);
}
const TECH_PASSWORD = process.env.DEMO_TECH_PASSWORD || 'DemoTech-2026!';
const LEADER_PASSWORD = process.env.DEMO_LEADER_PASSWORD || 'DemoLeader-2026!';
const LEAD_PASSWORD = process.env.DEMO_LEAD_PASSWORD || 'DemoLead-2026!';
const COMPANY_CODE = 'DEMO-UNIT';
const OUT = path.resolve('logs/demo-seed.json');

let token = '';
let adminToken = '';
const summary = { company: null, systems: [], assets: [], campaigns: [], maintainances: [], users: {} };

const sleep = (ms) => new Promise(resolve => setTimeout(resolve, ms));

async function api(method, pathName, body, expect = [200, 201]) {
  const res = await fetch(`${BASE}${pathName}`, {
    method,
    headers: {
      ...(token ? { Authorization: `Bearer ${token}` } : {}),
      ...(body !== undefined ? { 'Content-Type': 'application/json' } : {}),
    },
    body: body !== undefined ? JSON.stringify(body) : undefined,
  });
  const text = await res.text();
  let data;
  try { data = text ? JSON.parse(text) : null; } catch { data = text; }
  if (!expect.includes(res.status)) {
    throw new Error(`${method} ${pathName} -> ${res.status}: ${typeof data === 'string' ? data : JSON.stringify(data)}`);
  }
  return data;
}

async function list(pathName) {
  const data = await api('GET', pathName);
  return data?.data ?? [];
}

function iso(date) {
  return new Date(date).toISOString();
}

function daysAgo(days) {
  const d = new Date();
  d.setUTCDate(d.getUTCDate() - days);
  d.setUTCHours(3, 0, 0, 0);
  return d;
}

async function login(username, password) {
  const data = await api('POST', '/auth/login', { username, password }, [200, 400, 401]);
  if (!data?.accessToken) throw new Error(`Login failed for ${username}`);
  token = data.accessToken;
}

async function ensureByName(pathName, field, value, createBody, pathCreate = pathName) {
  const rows = await list(pathName);
  const found = rows.find(row => String(row[field] ?? '').toLowerCase() === String(value).toLowerCase());
  if (found) return found;
  const created = await api('POST', pathCreate, createBody);
  return created?.data ?? created;
}

async function ensureCode(pathName, code, createBody) {
  const rows = await list(pathName);
  const found = rows.find(row => row.code === code);
  if (found) return found;
  const created = await api('POST', pathName, createBody);
  return created?.data ?? created;
}

async function main() {
  // [DF-6] A caller may supply a short-lived admin token to make a verification re-run free of
  // auth-session side effects (auth_login_attempts / user_credentials). Normal use still logs in.
  if (process.env.DEMO_ADMIN_TOKEN) {
    token = process.env.DEMO_ADMIN_TOKEN;
    adminToken = token;
  } else {
    await login('admin', ADMIN_PASSWORD);
    adminToken = token;
  }

  // 1 unit, 3 departments, 3 locations
  const company = await ensureCode('/companies', COMPANY_CODE, {
    name: 'Đơn vị Kỹ thuật Bảo đảm',
    parentId: null,
    code: COMPANY_CODE,
  });
  summary.company = company.id;
  const companyId = company.id;

  const departmentNames = ['Phòng Kỹ thuật', 'Phòng Vận hành', 'Phòng Hành chính'];
  const departments = [];
  for (const name of departmentNames) {
    departments.push(await ensureByName('/departments', 'name', name, {
      name, companyId, managerId: null, phone: null, fax: null,
    }));
  }

  const locationNames = ['Phòng máy trung tâm', 'Trạm radar', 'Kho thiết bị'];
  const locations = [];
  for (const name of locationNames) {
    locations.push(await ensureByName('/locations', 'name', name, {
      name, parentId: null, companyId, managerId: null,
      address: 'Khu kỹ thuật', city: 'Hà Nội', state: null, country: 'Vietnam', zip: null,
    }));
  }
  const labLocation = locations[0];

  // Categories / manufacturers / models
  const catAsset = await ensureByName('/categories', 'name', 'Thiết bị CNTT', {
    name: 'Thiết bị CNTT', categoryType: 'Asset', tagColor: '#1677ff',
    checkinEmail: false, requireAcceptance: false, useDefaultEula: true, notes: 'Demo',
  });
  const catConsumable = await ensureByName('/categories', 'name', 'Vật tư kỹ thuật', {
    name: 'Vật tư kỹ thuật', categoryType: 'Consumable', tagColor: '#fa8c16',
    checkinEmail: false, requireAcceptance: false, useDefaultEula: true, notes: 'Demo',
  });
  const catComponent = await ensureByName('/categories', 'name', 'Linh kiện thay thế', {
    name: 'Linh kiện thay thế', categoryType: 'Component', tagColor: '#722ed1',
    checkinEmail: false, requireAcceptance: false, useDefaultEula: true, notes: 'Demo',
  });
  const catAccessory = await ensureByName('/categories', 'name', 'Phụ kiện kỹ thuật', {
    name: 'Phụ kiện kỹ thuật', categoryType: 'Accessory', tagColor: '#13c2c2',
    checkinEmail: false, requireAcceptance: false, useDefaultEula: true, notes: 'Demo',
  });
  const catLicense = await ensureByName('/categories', 'name', 'Phần mềm & bản quyền', {
    name: 'Phần mềm & bản quyền', categoryType: 'License', tagColor: '#eb2f96',
    checkinEmail: false, requireAcceptance: false, useDefaultEula: true, notes: 'Demo',
  });

  const manufacturerNames = ['Cisco', 'HPE', 'Dell', 'APC', 'Fortinet', 'MikroTik'];
  const manufacturers = [];
  for (const name of manufacturerNames) {
    manufacturers.push(await ensureByName('/manufacturers', 'name', name, {
      name, code: name.toUpperCase().replace(/[^A-Z0-9]/g, '').slice(0, 5),
      url: null, supportUrl: null, supportEmail: null,
    }));
  }
  const mfr = Object.fromEntries(manufacturers.map(m => [m.name, m.id]));

  const modelDefs = [
    ['Catalyst 9200L', 'C9200L-24P', 'Cisco'],
    ['ProLiant DL380', 'DL380-G10', 'HPE'],
    ['PowerEdge R650', 'R650', 'Dell'],
    ['Smart-UPS 3000', 'SMT3000', 'APC'],
    ['FortiGate 100F', 'FG-100F', 'Fortinet'],
    ['CCR2004 Router', 'CCR2004', 'MikroTik'],
    ['OptiPlex 7010', '7010', 'Dell'],
    ['ThinkVision 24', 'T24i', 'Dell'],
  ];
  const models = [];
  for (const [name, modelNumber, brand] of modelDefs) {
    models.push(await ensureByName('/models', 'name', name, {
      name, modelNumber, manufacturerId: mfr[brand], categoryId: catAsset.id,
      depreciationId: null, fieldsetId: null, eol: null, notes: 'Demo', requestable: false,
    }));
  }
  const model = Object.fromEntries(models.map(m => [m.name, m.id]));

  // 5 systems + 3-6 positions each
  const systemDefs = [
    ['SYS-2026-101', 'Radar giám sát', ['Tủ rack R1', 'Phòng điều khiển', 'Cột anten', 'Máy chủ tín hiệu']],
    ['SYS-2026-102', 'Thông tin VHF', ['Tủ rack V1', 'Bàn điều hành', 'Cột anten VHF']],
    ['SYS-2026-103', 'Mạng LAN/WAN', ['Tủ rack mạng', 'Phòng máy core', 'Phòng máy biên', 'Tủ phân phối', 'Phòng kỹ thuật']],
    ['SYS-2026-104', 'Ghi âm', ['Tủ rack ghi âm', 'Bàn trực ca', 'Kho lưu trữ']],
    ['SYS-2026-105', 'Camera giám sát', ['Tủ rack camera', 'Hành lang A', 'Hành lang B', 'Cổng vào', 'Phòng máy', 'Kho thiết bị']],
  ];
  const systems = [];
  let positionSeq = 0;
  const existingSystems = await list('/system-infos');
  for (const [code, name, positionNames] of systemDefs) {
    const sys = await ensureCode('/system-infos', code, {
      code, name, description: 'Hệ thống demo', companyId,
    });
    const positions = [];
    const systemDetail = (await api('GET', `/system-infos/${sys.id}`)).data;
    for (let i = 0; i < positionNames.length; i++) {
      const posCode = `POS-2026-${String(++positionSeq).padStart(3, '0')}`;
      let pos = (systemDetail.positions ?? []).find(p => p.name === positionNames[i]);
      if (!pos) {
        pos = (await api('POST', `/system-infos/${sys.id}/positions`, {
          code: posCode, name: positionNames[i], description: 'Vị trí demo',
        })).data;
      }
      positions.push(pos);
    }
    systems.push({ ...sys, positions });
  }
  summary.systems = systems.map(s => ({ id: s.id, code: s.code, name: s.name, positions: s.positions }));
  const allPositions = systems.flatMap(s => s.positions);

  // Asset tags 001..060
  const existingAssets = await list('/assets?pageSize=1000');
  const byTag = new Map(existingAssets.map(a => [a.assetTag, a]));
  const assetNames = ['Switch', 'Router', 'Firewall', 'Server', 'UPS', 'Máy trạm', 'Màn hình', 'Bộ thu phát'];
  const assetModels = ['Catalyst 9200L', 'CCR2004 Router', 'FortiGate 100F', 'ProLiant DL380', 'Smart-UPS 3000', 'OptiPlex 7010', 'ThinkVision 24', 'ProLiant DL380'];
  for (let i = 0; i < 60; i++) {
    const tag = `DEMO-${String(i + 1).padStart(3, '0')}`;
    if (byTag.has(tag)) { summary.assets.push(byTag.get(tag)); continue; }
    const name = `${assetNames[i % assetNames.length]} ${String(i + 1).padStart(2, '0')}`;
    const modelName = assetModels[i % assetModels.length];
    const created = await api('POST', '/assets', {
      assetTag: tag, name,
      serial: `SN-DEMO-${String(i + 1).padStart(3, '0')}`,
      modelId: model[modelName], locationId: labLocation.id, supplierId: null, companyId,
      purchaseCost: 12000000 + i * 250000,
      purchaseDate: iso(daysAgo(30 + (i * 11) % 300)),
      warrantyMonths: 24, orderNumber: `PO-DEMO-${i + 1}`,
      notes: 'Thiết bị demo', physical: true, requestable: false,
    });
    // [DF-6] The create response may omit the computed status (or return the enum's numeric
    // default). A fresh asset is Pending; normalise it so the history block below runs exactly
    // once per asset instead of being skipped on run 1 and executed on run 2.
    const asset = created?.data ?? created;
    if (asset && (asset.status == null || asset.status === 0
      || String(asset.status).toLowerCase() === 'pending')) {
      asset.status = 'Pending';
    }
    summary.assets.push(asset);
  }

  // Install / check-in / transfer history for 22 assets.
  const techUsers = [];
  for (let i = 0; i < 22; i++) {
    const a = summary.assets[i];
    if (!a?.id || a.status !== 'Pending') continue;
    const p1 = allPositions[i % allPositions.length];
    try {
      if (i < 12) {
        await api('POST', `/assets/${a.id}/checkout`, {
          targetType: 'SystemPosition', targetId: p1.id,
          note: 'Lắp đặt ban đầu', checkoutAt: iso(daysAgo(280 - i * 4)), locationId: labLocation.id,
        });
        await api('POST', `/assets/${a.id}/checkin`, {
          locationId: labLocation.id, note: 'Thu hồi bảo dưỡng', checkinAt: iso(daysAgo(160 - i * 2)),
        });
        const p2 = allPositions[(i + 3) % allPositions.length];
        await api('POST', `/assets/${a.id}/checkout`, {
          targetType: 'SystemPosition', targetId: p2.id,
          note: 'Điều chuyển hệ thống', checkoutAt: iso(daysAgo(110 - i * 2)), locationId: labLocation.id,
        });
      } else {
        await api('POST', `/assets/${a.id}/checkout`, {
          targetType: 'SystemPosition', targetId: p1.id,
          note: 'Lắp đặt hiện tại', checkoutAt: iso(daysAgo(90 - i)), locationId: labLocation.id,
        });
      }
    } catch (err) {
      console.warn(`asset history ${a.assetTag}: ${err.message}`);
    }
  }

  // Users + groups (company-scoped)
  async function ensureUser(username, firstName, lastName, email, groupName, permissions) {
    const users = await list('/users');
    let user = users.find(u => u.username === username);
    let createdNow = false;
    if (!user) {
      const created = await api('POST', '/users', {
        username, email, firstName, lastName, employeeNumber: null, jobTitle: null,
        password: 'DemoInitial-2026!', isSuperUser: false, isActive: true,
        companyId, departmentId: departments[0].id, locationId: labLocation.id,
      });
      user = created.data;
      createdNow = true;
    }
    const groups = await list('/groups');
    let group = groups.find(g => g.name === groupName);
    if (!group) {
      const created = await api('POST', '/groups', { name: groupName, description: 'Nhóm demo' });
      group = created.data;
    }
    // [DF-6] PUT only when the grant set actually changes. Re-sending an identical permission
    // payload wrote an ActionLog row on every seed run, so run2 never matched run1.
    const desired = [...permissions].sort();
    const current = (group.permissions ?? [])
      .filter(p => p.value === 1 || p.value === 'Grant')
      .map(p => p.permissionKey)
      .sort();
    if (JSON.stringify(current) !== JSON.stringify(desired)) {
      await api('PUT', `/groups/${group.id}/permissions`, permissions.map(key => ({ permissionKey: key, value: 'Grant' })));
    }
    const currentGroupIds = (user.groups ?? []).map(g => g.groupId).sort();
    if (JSON.stringify(currentGroupIds) !== JSON.stringify([group.id])) {
      await api('PUT', `/users/${user.id}/groups`, { groupIds: [group.id] });
    }
    // [DF-6] Only set the initial password on the run that created the account. Re-running the
    // old "login with DemoInitial + change password" block added an auth_login_attempts row (and
    // sometimes a credential row) on every seed, breaking run1 == run2.
    if (createdNow) {
      try {
        await login(username, 'DemoInitial-2026!');
        await api('POST', '/auth/password', {
          currentPassword: 'DemoInitial-2026!',
          newPassword: username === 'demo-tech'
            ? TECH_PASSWORD
            : (username === 'demo-lead' ? LEAD_PASSWORD : LEADER_PASSWORD),
        });
      } catch (err) {
        console.warn(`demo password ${username}: ${err.message}`);
      }
    }
    // A login above would replace the global token with the demo user; restore the admin token.
    token = adminToken;
    return user.id;
  }

  const techId = await ensureUser('demo-tech', 'Ky thuat', 'Vien', 'demo-tech@example.invalid', 'Kỹ thuật viên', [
    // [DF-6] Technician: inventory intake + audit + stock checkout, and maintenance execution.
    // [DF-6c] Need read-only lookup data to complete intake via UI; companies.view is deliberately
    // NOT granted — the create form must hide/omit company for a company-scoped user.
    'assets.view', 'assets.create', 'assets.edit', 'assets.checkout', 'assets.checkin', 'assets.audit',
    'systems.view', 'consumables.checkout', 'components.checkout',
    'models.view', 'locations.view', 'suppliers.view', 'categories.view',
    'maintenance.view', 'maintenance.campaigns', 'maintenance.tickets',
  ]);
  const leaderId = await ensureUser('demo-leader', 'Lanh dao', 'Don vi', 'demo-leader@example.invalid', 'Lãnh đạo', [
    'assets.view', 'systems.view', 'reports.view', 'export',
  ]);
  // [DF-7] Tổ trưởng approves/closes tickets created by technicians.
  const leadId = await ensureUser('demo-lead', 'To truong', 'Don vi', 'demo-lead@example.invalid', 'Tổ trưởng', [
    'assets.view', 'maintenance.tickets', 'maintenance.tickets.approve',
  ]);
  summary.users = { techId, leaderId, leadId };

  // Licenses, consumables, components, accessories
  const licenses = [];
  for (const [name, serial, days] of [
    ['Bản quyền giám sát', 'LIC-DEMO-001', -10],
    ['Bản quyền văn phòng', 'LIC-DEMO-002', 20],
    ['Bản quyền sao lưu', 'LIC-DEMO-003', 200],
  ]) {
    const rows = await list('/licenses?pageSize=1000');
    let row = rows.find(x => x.name === name);
    if (!row) {
      const created = await api('POST', '/licenses', {
        name, serial, seats: 5, reassignable: true,
        expirationDate: iso(daysAgo(-days)), terminationDate: null,
        purchaseCost: 5000000, purchaseDate: iso(daysAgo(180)), orderNumber: null,
        minSeats: 1, notes: 'Demo', supplierId: null, manufacturerId: null,
        categoryId: catLicense.id, companyId,
      });
      row = created.data;
    }
    licenses.push(row);
  }

  for (const [name, itemNo, qty, min] of [
    ['Cáp mạng Cat6', 'VT-CAT6', 3, 10],
    ['Đầu nối quang', 'VT-QUANG', 2, 5],
    ['Pin UPS', 'VT-PIN', 4, 6],
  ]) {
    const rows = await list('/consumables?pageSize=1000');
    if (!rows.find(x => x.itemNo === itemNo)) {
      await api('POST', '/consumables', {
        name, itemNo, qty, minAmt: min, categoryId: catConsumable.id,
        manufacturerId: null, supplierId: null, locationId: labLocation.id, companyId,
        modelNumber: null, orderNumber: null, purchaseCost: 100000, purchaseDate: iso(daysAgo(30)),
        notes: 'Demo', image: null,
      });
    }
  }

  for (const [name, itemNo, qty, min] of [
    ['Quạt làm mát', 'LK-FAN', 2, 4],
    ['Nguồn dự phòng', 'LK-PSU', 1, 3],
  ]) {
    const rows = await list('/components?pageSize=1000');
    if (!rows.find(x => x.name === name)) {
      await api('POST', '/components', {
        name, serial: null, qty, minAmt: min, categoryId: catComponent.id,
        locationId: labLocation.id, companyId, supplierId: null, manufacturerId: null,
        modelNumber: null, orderNumber: null, purchaseCost: 500000, purchaseDate: iso(daysAgo(30)),
        notes: 'Demo', trackingType: 'Bulk', serialNumbers: null,
      });
    }
  }

  for (const [name, itemNo, qty, min] of [
    ['Bộ nguồn di động', 'PK-PSU', 1, 3],
    ['Đầu chuyển quang', 'PK-SFP', 2, 5],
  ]) {
    const rows = await list('/accessories?pageSize=1000');
    if (!rows.find(x => x.name === name)) {
      await api('POST', '/accessories', {
        name, itemNo, qty, minAmt: min, categoryId: catAccessory.id,
        manufacturerId: null, supplierId: null, locationId: labLocation.id, companyId,
        modelNumber: null, orderNumber: null, purchaseCost: 300000, purchaseDate: iso(daysAgo(30)),
        notes: 'Demo', image: null,
      });
    }
  }

  // Repair tickets (13) across two quarters.
  const repairRows = await list('/maintenances?pageSize=1000');
  const types = ['Repair', 'IncidentReport', 'HardwareSupport'];
  for (let i = 0; i < 13; i++) {
    const title = `Phiếu sửa chữa DEMO ${String(i + 1).padStart(2, '0')}`;
    const existing = repairRows.find(r => r.title === title);
    if (existing) { summary.maintainances.push(existing); continue; }
    const asset = summary.assets[(i + 2) % summary.assets.length];
    const start = daysAgo(220 - i * 12);
    const completed = i % 3 !== 0;
    try {
      const created = await api('POST', `/assets/${asset.id}/maintenances`, {
        type: types[i % types.length],
        title,
        notes: 'Sự cố demo trong vận hành',
        supplierId: null,
        startDate: iso(start),
        completionDate: completed ? iso(daysAgo(215 - i * 12)) : null,
        cost: i % 4 === 0 ? null : 150000 + i * 50000,
        isWarranty: i % 5 === 0,
        assigneeUserIds: [techId],
      });
      summary.maintainances.push(created.data ?? created);
    } catch (err) {
      console.warn(`maintenance ${title}: ${err.message}`);
    }
  }

  // One published template per system (campaigns are system-scoped).
  async function ensureTemplate(sys) {
    const rows = await list('/maintenance/templates');
    let t = rows.find(x => x.systemInfo?.id === sys.id);
    if (t) return t;
    t = (await api('POST', '/maintenance/templates', {
      name: `Mẫu bảo dưỡng ${sys.name}`, systemInfoId: sys.id, companyId,
    })).data;
    const version = (await api('POST', `/maintenance/templates/${t.id}/versions`, { effectiveFrom: null })).data;
    const item = (await api('POST', `/maintenance/templates/${t.id}/versions/${version.id}/items`, {
      order: 1, name: 'Vệ sinh và kiểm tra nguồn', cycleMonths: 3,
      toolsRequired: 'Khí nén, đồng hồ vạn năng',
      instruction: 'Vệ sinh thiết bị, kiểm tra điện áp và các đầu nối.', positionIds: [],
    })).data;
    await api('POST', `/maintenance/templates/${t.id}/versions/${version.id}/items/${item.id}/standard-params`, {
      paramName: 'Điện áp đầu ra', nominalValue: '220', thresholdOperator: 'LessOrEqual',
      thresholdValue: 230, checkMethod: 'Đo tại ổ cắm', unit: 'V',
    });
    await api('POST', `/maintenance/templates/${t.id}/versions/${version.id}/publish`, {});
    return t;
  }

  const campaigns = await list('/maintenance/campaigns?pageSize=100');
  const campaignDefs = [
    [systems[0], 'DOT-DEMO-Q1', 270, true],
    [systems[1], 'DOT-DEMO-Q2', 90, false],
    [systems[2], 'DOT-DEMO-Q3', 20, false],
  ];
  for (const [sys, batch, days, complete] of campaignDefs) {
    let campaign = campaigns.find(c => c.batchNumber === batch);
    if (!campaign) {
      const templateForSystem = await ensureTemplate(sys);
      campaign = (await api('POST', '/maintenance/campaigns', {
        systemInfoId: sys.id, templateId: templateForSystem.id,
        startDate: iso(daysAgo(days)), endDate: null, batchNumber: batch,
        reviewerId: null, executorIds: [techId],
      })).data;
    }
    // [DF-6] Only complete once. Re-posting /complete on an already Completed campaign is an
    // upsert-shaped no-op at best; skip it explicitly so a re-run cannot append audit noise.
    if (complete && campaign.status !== 'Completed') {
      try { await api('POST', `/maintenance/campaigns/${campaign.id}/complete`, { forceIncomplete: true }); } catch { /* already complete */ }
    }
    summary.campaigns.push(campaign);
  }

  // [DF-7b] The create response on run 1 has a different shape than the list response on run 2.
  // Normalize the summary from the API so two consecutive seed runs are byte-identical.
  summary.assets = (await list('/assets?pageSize=1000'))
    .sort((a, b) => String(a.assetTag).localeCompare(String(b.assetTag)));

  fs.mkdirSync(path.dirname(OUT), { recursive: true });
  fs.writeFileSync(OUT, JSON.stringify(summary, null, 2), 'utf8');
  console.log(`Seeded demo unit=${companyId}, systems=${summary.systems.length}, assets=${summary.assets.length}, users=${JSON.stringify(summary.users)}`);
  console.log(`Summary written to ${OUT}`);
}

main().catch(err => { console.error(err); process.exit(1); });
