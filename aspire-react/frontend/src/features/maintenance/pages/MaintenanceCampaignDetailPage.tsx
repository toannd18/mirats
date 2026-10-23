import { useCallback, useEffect, useMemo, useState } from 'react';
import {
  Alert, App, Button, Card, Collapse, Descriptions, Input, Space, Spin, Switch,
  Table, Tag, Tooltip, Typography,
} from 'antd';
import {
  ArrowLeftOutlined, CheckCircleOutlined, CarOutlined, ClusterOutlined, ExperimentOutlined, UserOutlined,
} from '@ant-design/icons';
import { useNavigate, useParams } from 'react-router-dom';
import apiClient from '../../../services/api-client';
import { formatDate, formatDateTime } from '../../../utils/format';
import { usePermission } from '../../../hooks/usePermission';
import { useIsMobile } from '../../../hooks/useIsMobile';
// [FE-R3] Màu UI mới đi qua token dùng chung (T-TOKEN1) — không hard-code hex trong page.
import { textColors, uiColors } from '../../../theme/designTokens';

interface CampaignDetailDto {
  id: string;
  systemInfoId: string;
  systemInfoName: string;
  templateVersionId: string;
  templateId: string;
  versionNumber: number;
  startDate?: string | null;
  endDate?: string | null;
  batchNumber?: string | null;
  status: string;
  createdAt: string;
  executors: Array<{ userId: string; fullName?: string }>;
  snapshots: Array<{
    id: string; assetId: string; assetTag: string; assetName: string;
    serial?: string | null; modelNumber?: string | null;
    systemPositionId?: string | null; systemPositionName?: string | null;
  }>;
  results: Array<{ id: string; deviceSnapshotId: string; checklistItemId: string; standardParamId?: string | null; measuredValue?: string | null; isPass: boolean; notes?: string | null }>;
}

interface StandardParamDto {
  id: string;
  paramName: string;
  nominalValue?: string | null;
  thresholdOperator?: string | null;
  thresholdValue?: number | null;
  checkMethod?: string | null;
  unit?: string | null;
}

interface ChecklistItemDto {
  id: string; order: number; name: string; cycleMonths: number;
  positionIds?: string[];
  positionNames?: Array<string | null>;
  standardParams?: StandardParamDto[];
}

interface ResultDraft { measuredValue?: string; isPass: boolean; notes?: string; }

const { Text } = Typography;

const THRESHOLD_OP_SYMBOL: Record<string, string> = {
  LessThan: '<', LessOrEqual: '≤', GreaterThan: '>', GreaterOrEqual: '≥', Equal: '=',
};

const isApplicablePair = (
  s: CampaignDetailDto['snapshots'][number],
  it: ChecklistItemDto,
): boolean => {
  const ids = it.positionIds ?? [];
  if (ids.length === 0) return true;
  return !!s.systemPositionId && ids.includes(s.systemPositionId);
};

function parseMeasured(raw?: string | null): number | null {
  if (!raw || raw.trim() === '') return null;
  const m = /-?\d+(?:[.,]\d+)?/.exec(raw.trim());
  if (!m) return null;
  const v = Number(m[0].replace(',', '.'));
  return Number.isFinite(v) ? v : null;
}

function evaluateIsPass(p: StandardParamDto | undefined | null, measuredRaw?: string | null): boolean | null {
  if (!p || p.thresholdOperator == null || p.thresholdValue == null) return null;
  const mv = parseMeasured(measuredRaw);
  if (mv === null) return null;
  const thr = p.thresholdValue;
  switch (p.thresholdOperator) {
    case 'LessThan': return mv < thr;
    case 'LessOrEqual': return mv <= thr;
    case 'GreaterThan': return mv > thr;
    case 'GreaterOrEqual': return mv >= thr;
    case 'Equal': return Math.abs(mv - thr) < 0.0001;
    default: return null;
  }
}

function formatThreshold(p?: StandardParamDto | null): string {
  if (!p || p.thresholdOperator == null || p.thresholdValue == null) return '—';
  const sym = THRESHOLD_OP_SYMBOL[p.thresholdOperator] ?? p.thresholdOperator;
  return `${sym} ${p.thresholdValue}${p.unit ? ` ${p.unit}` : ''}`;
}

interface ResultRow {
  key: string;
  snapshot: CampaignDetailDto['snapshots'][number];
  param: StandardParamDto | null;
  paramId: string | null;
}

export default function MaintenanceCampaignDetailPage() {
  const { message, modal } = App.useApp();
  const navigate = useNavigate();
  const { id } = useParams<{ id: string }>();
  const isMobile = useIsMobile();
  const canManage = usePermission('maintenance.campaigns');

  const [campaign, setCampaign] = useState<CampaignDetailDto | null>(null);
  const [items, setItems] = useState<ChecklistItemDto[]>([]);
  const [loading, setLoading] = useState(true);
  // [DF-2] A failed checklist request is an ERROR, never an empty checklist. 403 (no permission)
  // and 5xx/network failures get distinct messages and a retry action.
  const [checklistError, setChecklistError] = useState<{ kind: 'forbidden' | 'error'; message: string } | null>(null);
  const [campaignError, setCampaignError] = useState<{ kind: 'forbidden' | 'notfound' | 'error'; message: string } | null>(null);
  const [drafts, setDrafts] = useState<Record<string, ResultDraft>>({});
  const [savingKeys, setSavingKeys] = useState<Set<string>>(new Set());
  const [savedKeys, setSavedKeys] = useState<Set<string>>(new Set());
  const [completing, setCompleting] = useState(false);

  const campaignId = id ?? '';

  const loadCampaign = useCallback(async () => {
    try {
      const res = await apiClient.get(`/maintenance/campaigns/${campaignId}`);
      setCampaign(res.data.data);
      setCampaignError(null);
      return res.data.data as CampaignDetailDto;
    } catch (err: unknown) {
      const e = err as { response?: { status?: number; data?: { message?: string } } };
      const status = e?.response?.status;
      setCampaign(null);
      if (status === 403) {
        setCampaignError({ kind: 'forbidden', message: 'Bạn không có quyền xem đợt bảo dưỡng này.' });
      } else if (status === 404) {
        setCampaignError({ kind: 'notfound', message: 'Đợt bảo dưỡng không tồn tại hoặc ngoài phạm vi công ty của bạn.' });
      } else {
        setCampaignError({ kind: 'error', message: e?.response?.data?.message || 'Lỗi tải đợt bảo dưỡng. Vui lòng thử lại.' });
      }
      return null;
    }
  }, [campaignId]);

  const loadChecklist = useCallback(async () => {
    setChecklistError(null);
    try {
      // [DF-1] Executor checklist endpoint: guarded by maintenance.campaigns, scoped server-side to
      // the campaign company. Do NOT call the template-version endpoint here — it requires
      // maintenance.templates and would 403 for technicians.
      const res = await apiClient.get(`/maintenance/campaigns/${campaignId}/checklist`);
      setItems((res.data.data ?? []) as ChecklistItemDto[]);
    } catch (err: unknown) {
      const e = err as { response?: { status?: number; data?: { message?: string } } };
      setItems([]);
      setChecklistError(
        e?.response?.status === 403
          ? { kind: 'forbidden', message: 'Bạn không có quyền xem danh mục kiểm tra của đợt bảo dưỡng này.' }
          : { kind: 'error', message: e?.response?.data?.message || 'Không tải được danh mục kiểm tra. Vui lòng thử lại.' },
      );
    }
  }, [campaignId]);

  useEffect(() => {
    if (!campaignId) return;
    let alive = true;
    (async () => {
      setLoading(true);
      const c = await loadCampaign();
      if (!alive || !c) { setLoading(false); return; }
      await loadChecklist();
      if (alive) setLoading(false);
    })();
    return () => { alive = false; };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [campaignId]);

  const keyOf = useCallback((snapshotId: string, itemId: string, paramId: string | null) => `${snapshotId}__${itemId}__${paramId ?? 'none'}`, []);

  useEffect(() => {
    if (!campaign) return;
    setDrafts(prev => {
      const next = { ...prev };
      for (const s of campaign.snapshots) {
        for (const it of items) {
          if (!isApplicablePair(s, it)) continue;
          const params = it.standardParams ?? [];
          if (params.length === 0) {
            const key = keyOf(s.id, it.id, null);
            if (!next[key]) {
              const saved = campaign.results.find(r => r.deviceSnapshotId === s.id && r.checklistItemId === it.id && !r.standardParamId);
              next[key] = saved
                ? { measuredValue: saved.measuredValue ?? '', isPass: saved.isPass, notes: saved.notes ?? '' }
                : { measuredValue: '', isPass: true, notes: '' };
            }
          } else {
            for (const p of params) {
              const key = keyOf(s.id, it.id, p.id);
              if (!next[key]) {
                const saved = campaign.results.find(r => r.deviceSnapshotId === s.id && r.checklistItemId === it.id && r.standardParamId === p.id);
                next[key] = saved
                  ? { measuredValue: saved.measuredValue ?? '', isPass: saved.isPass, notes: saved.notes ?? '' }
                  : { measuredValue: '', isPass: true, notes: '' };
              }
            }
          }
        }
      }
      return next;
    });
  }, [campaign, items, keyOf]);

  const updateDraft = (key: string, patch: Partial<ResultDraft>) => {
    setDrafts(prev => ({ ...prev, [key]: { ...prev[key], ...patch } }));
    setSavedKeys(prev => { const n = new Set(prev); n.delete(key); return n; });
  };

  const isDirty = useCallback((snapshotId: string, itemId: string, paramId: string | null, param?: StandardParamDto | null): boolean => {
    if (!campaign) return false;
    const key = keyOf(snapshotId, itemId, paramId);
    const draft = drafts[key];
    if (!draft) return false;
    const saved = campaign.results.find(r => r.deviceSnapshotId === snapshotId && r.checklistItemId === itemId && (r.standardParamId ?? null) === (paramId ?? null));
    if (!saved) return draft.measuredValue !== '' || draft.isPass !== true || !!draft.notes;
    if (paramId) {
      const auto = evaluateIsPass(param, draft.measuredValue);
      const savedAuto = evaluateIsPass(param, saved.measuredValue);
      return (saved.measuredValue ?? '') !== (draft.measuredValue ?? '')
        || (saved.notes ?? '') !== (draft.notes ?? '')
        || (auto !== null ? auto : saved.isPass) !== (savedAuto ?? saved.isPass);
    }
    return (saved.measuredValue ?? '') !== draft.measuredValue
      || saved.isPass !== draft.isPass
      || (saved.notes ?? '') !== draft.notes;
  }, [campaign, drafts, keyOf]);

  const saveRow = async (snapshotId: string, itemId: string, paramId: string | null, silent = false) => {
    const key = keyOf(snapshotId, itemId, paramId);
    const draft = drafts[key];
    if (!draft) return;
    setSavingKeys(prev => new Set(prev).add(key));
    try {
      await apiClient.post(`/maintenance/campaigns/${campaignId}/results`, {
        deviceSnapshotId: snapshotId,
        checklistItemId: itemId,
        standardParamId: paramId ?? null,
        measuredValue: draft.measuredValue === '' ? null : draft.measuredValue,
        isPass: draft.isPass,
        notes: draft.notes === '' ? null : draft.notes,
      });
      setSavedKeys(prev => new Set(prev).add(key));
      if (!silent) message.success('Đã lưu');
      await loadCampaign();
    } catch (err: unknown) {
      const e = err as { response?: { data?: { message?: string } } };
      message.error(e?.response?.data?.message || 'Lỗi lưu kết quả');
    } finally {
      setSavingKeys(prev => { const n = new Set(prev); n.delete(key); return n; });
    }
  };

  const completed = campaign?.status === 'Completed';
  const editableRows = canManage && !completed;

  const applicableTotal = useMemo(() => {
    if (!campaign) return 0;
    return items.reduce((sum, it) => {
      const applicableSnap = campaign.snapshots.filter(s => isApplicablePair(s, it)).length;
      const factor = (it.standardParams?.length ?? 0) === 0 ? 1 : (it.standardParams!.length);
      return sum + applicableSnap * factor;
    }, 0);
  }, [campaign, items]);
  const recordedTotal = useMemo(() => {
    if (!campaign) return 0;
    const keys = new Set(campaign.results.map(r => `${r.deviceSnapshotId}__${r.checklistItemId}__${r.standardParamId ?? 'none'}`));
    let n = 0;
    for (const s of campaign.snapshots)
      for (const it of items) {
        if (!isApplicablePair(s, it)) continue;
        const params = it.standardParams ?? [];
        if (params.length === 0) {
          if (keys.has(`${s.id}__${it.id}__none`)) n += 1;
        } else {
          for (const p of params) if (keys.has(`${s.id}__${it.id}__${p.id}`)) n += 1;
        }
      }
    return n;
  }, [campaign, items]);
  const allRecorded = applicableTotal === 0 || recordedTotal >= applicableTotal;
  const hasChecklist = items.length > 0;
  const missingResults = Math.max(0, applicableTotal - recordedTotal);

  const fmtDate = (v?: string | null) => formatDateTime(v);

  const panelDirty = useMemo(() => {
    const map = new Map<string, number>();
    if (!campaign) return map;
    for (const s of campaign.snapshots) {
      for (const it of items) {
        if (!isApplicablePair(s, it)) continue;
        const params = it.standardParams ?? [];
        if (params.length === 0) {
          if (isDirty(s.id, it.id, null)) map.set(it.id, (map.get(it.id) ?? 0) + 1);
        } else {
          for (const p of params) if (isDirty(s.id, it.id, p.id, p)) map.set(it.id, (map.get(it.id) ?? 0) + 1);
        }
      }
    }
    return map;
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [campaign, items, drafts, isDirty]);

  const doComplete = async (forceIncomplete = false) => {
    setCompleting(true);
    try {
      const res = await apiClient.post(`/maintenance/campaigns/${campaignId}/complete`, {
        forceIncomplete,
      });
      const due = res.data?.data?.nextMaintenanceDueDate;
      message.success(`Đã hoàn thành đợt bảo dưỡng${due ? ` — lần bảo dưỡng tới: ${formatDate(due)}` : ''}`);
      await loadCampaign();
    } catch (err: unknown) {
      const e = err as { response?: { data?: { message?: string; error_code?: string } } };
      message.error(e?.response?.data?.message || 'Không thể hoàn thành đợt bảo dưỡng');
    } finally {
      setCompleting(false);
    }
  };

  if (loading && !campaign) return <div style={{ textAlign: 'center', padding: 64 }}><Spin size="large" /></div>;

  if (!campaign) {
    const kind = campaignError?.kind ?? 'error';
    return (
      <div>
        <Alert
          type={kind === 'forbidden' ? 'error' : kind === 'notfound' ? 'warning' : 'error'}
          showIcon
          title={campaignError?.message ?? 'Lỗi tải đợt bảo dưỡng.'}
          action={kind === 'error'
            ? <Button size="small" onClick={() => {
              setLoading(true);
              void (async () => {
                const c = await loadCampaign();
                if (c) await loadChecklist();
                setLoading(false);
              })();
            }}>Thử lại</Button>
            : undefined}
        />
        <Button icon={<ArrowLeftOutlined />} style={{ marginTop: 16 }} onClick={() => navigate('/maintenance/campaigns')}>
          Quay lại danh sách
        </Button>
      </div>
    );
  }

  const collapseItems = items.map(it => {
    const applicableSnapshots = campaign.snapshots.filter(s => isApplicablePair(s, it));
    const params = it.standardParams ?? [];
    const hasParams = params.length > 0;
    const expectedRows = applicableSnapshots.length * (hasParams ? params.length : 1);
    const resultKeys = new Set(campaign.results.map(r => `${r.deviceSnapshotId}__${r.checklistItemId}__${r.standardParamId ?? 'none'}`));
    let doneForItem = 0;
    for (const s of applicableSnapshots) {
      if (hasParams) {
        for (const p of params) if (resultKeys.has(`${s.id}__${it.id}__${p.id}`)) doneForItem += 1;
      } else {
        if (resultKeys.has(`${s.id}__${it.id}__none`)) doneForItem += 1;
      }
    }
    const dirtyCount = panelDirty.get(it.id) ?? 0;
    const positionNames = (it.positionNames ?? []).filter((n): n is string => !!n);

    const rows: ResultRow[] = [];
    for (const s of applicableSnapshots) {
      if (!hasParams) rows.push({ key: keyOf(s.id, it.id, null), snapshot: s, param: null, paramId: null });
      else for (const p of params) rows.push({ key: keyOf(s.id, it.id, p.id), snapshot: s, param: p, paramId: p.id });
    }

    return {
      key: it.id,
      label: (
        <Space wrap size={8}>
          <Text strong>{it.order}. {it.name}</Text>
          <Tag color={it.cycleMonths <= 3 ? 'orange' : 'blue'}>{it.cycleMonths} tháng</Tag>
          {positionNames.length === 0
            ? <Tag>Mọi vị trí</Tag>
            : <Tag color="blue">{positionNames.join(', ')}</Tag>}
          {hasParams && <Tag icon={<ExperimentOutlined />} color="purple">{params.length} tiêu chuẩn</Tag>}
          <Tag color={doneForItem >= expectedRows ? 'success' : 'warning'}>
            {doneForItem}/{expectedRows} {hasParams ? 'kết quả' : 'thiết bị'}
          </Tag>
          {dirtyCount > 0 && <Tag color="processing">{dirtyCount} chưa lưu</Tag>}
        </Space>
      ),
      children: (
        <>
          {editableRows && (
            <Space style={{ marginBottom: 8 }} wrap>
              <Button
                size="small"
                type="primary"
                disabled={dirtyCount === 0}
                onClick={async () => {
                  for (const r of rows) {
                    if (isDirty(r.snapshot.id, it.id, r.paramId, r.param)) await saveRow(r.snapshot.id, it.id, r.paramId, true);
                  }
                  message.success(`Đã lưu nhóm "${it.name}"`);
                }}
              >
                Lưu nhóm này{dirtyCount > 0 ? ` (${dirtyCount})` : ''}
              </Button>
              {!isMobile && <span style={{ fontSize: 12, color: textColors.secondary }}>Nhập xong từng dòng có thể bấm “Lưu” để lưu ngay dòng đó.</span>}
            </Space>
          )}
          {isMobile ? (
            <div style={{ display: 'grid', gap: 10 }}>
              {rows.map(row => {
                const k = row.key;
                const dirty = isDirty(row.snapshot.id, it.id, row.paramId, row.param);
                const saved = savedKeys.has(k);
                return (
                  <Card key={k} size="small" style={{ borderRadius: 10 }}>
                    <Space style={{ width: '100%', justifyContent: 'space-between' }} wrap>
                      <Text strong>{row.snapshot.assetName} <Text code style={{ fontSize: 11 }}>{row.snapshot.assetTag}</Text></Text>
                      <Text type="secondary" style={{ fontSize: 11 }}>{row.snapshot.systemPositionName || '—'}</Text>
                    </Space>
                    <Text type="secondary" style={{ display: 'block', fontSize: 11, marginTop: 2 }}>
                      SN: {row.snapshot.serial || '—'}
                    </Text>
                    {hasParams && row.param && (
                      <div style={{ marginTop: 8, padding: '8px 10px', background: '#fafafa', borderRadius: 8 }}>
                        <Text strong><ExperimentOutlined style={{ marginRight: 4, color: uiColors.accentPurple }} />{row.param.paramName}</Text>
                        <div style={{ fontSize: 11, color: textColors.secondary }}>Ngưỡng: {formatThreshold(row.param)}</div>
                      </div>
                    )}
                    <div style={{ display: 'grid', gap: 8, marginTop: 10 }}>
                      {hasParams && (
                        <div>
                          <Text type="secondary" style={{ display: 'block', fontSize: 12, marginBottom: 4 }}>Giá trị đo</Text>
                          {editableRows ? (
                            <Input
                              value={drafts[k]?.measuredValue ?? ''}
                              onChange={e => updateDraft(k, { measuredValue: e.target.value })}
                              placeholder={row.param?.unit ? `VD: 45 ${row.param.unit}` : 'VD: 45'}
                              style={{ width: '100%' }}
                            />
                          ) : <Text>{drafts[k]?.measuredValue || '—'}</Text>}
                        </div>
                      )}
                      <div style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
                        <Text type="secondary" style={{ fontSize: 12 }}>Kết quả</Text>
                        {hasParams && row.param ? (
                          (() => {
                            const auto = evaluateIsPass(row.param, drafts[k]?.measuredValue);
                            if (auto === null) return <Tag>Chưa xác định</Tag>;
                            return auto ? <Tag color="success">Đạt</Tag> : <Tag color="error">Không đạt</Tag>;
                          })()
                        ) : editableRows ? (
                          <Switch
                            checkedChildren="Đạt"
                            unCheckedChildren="Không đạt"
                            checked={drafts[k]?.isPass ?? true}
                            onChange={v => updateDraft(k, { isPass: v })}
                          />
                        ) : ((drafts[k]?.isPass ?? true) ? <Tag color="success">Đạt</Tag> : <Tag color="error">Không đạt</Tag>)}
                      </div>
                      <div>
                        <Text type="secondary" style={{ display: 'block', fontSize: 12, marginBottom: 4 }}>Ghi chú</Text>
                        {editableRows ? (
                          <Input
                            value={drafts[k]?.notes ?? ''}
                            onChange={e => updateDraft(k, { notes: e.target.value })}
                            placeholder="Ghi chú"
                            style={{ width: '100%' }}
                          />
                        ) : <Text>{drafts[k]?.notes || '—'}</Text>}
                      </div>
                      {editableRows && (
                        <Space>
                          <Button size="small" type="primary" disabled={!dirty} loading={savingKeys.has(k)} onClick={() => saveRow(row.snapshot.id, it.id, row.paramId)}>
                            Lưu dòng
                          </Button>
                          {saved && !dirty && <Tag color="success">Đã lưu</Tag>}
                        </Space>
                      )}
                    </div>
                  </Card>
                );
              })}
            </div>
          ) : (
            <Table<ResultRow>
              rowKey="key"
              size="small"
              pagination={false}
              scroll={{ x: 'max-content' }}
              dataSource={rows}
              locale={{ emptyText: 'Không có thiết bị nào thuộc phạm vi hạng mục này.' }}
              columns={[
                {
                  title: 'Thiết bị', key: 'device', width: hasParams ? 170 : 220,
                  onCell: (_: ResultRow, index?: number) => {
                    if (!hasParams) return {};
                    const pc = params.length;
                    if (index === undefined) return {};
                    return index % pc === 0 ? { rowSpan: pc } : { rowSpan: 0 };
                  },
                  render: (_: unknown, row: ResultRow) => (
                    <Space size={4} align="start">
                      <CarOutlined style={{ color: uiColors.labelGray, marginTop: 4 }} />
                      <div>
                        <div>{row.snapshot.assetName} <Text code style={{ fontSize: 11 }}>{row.snapshot.assetTag}</Text></div>
                        <div style={{ fontSize: 11, color: textColors.secondary }}>
                          SN: {row.snapshot.serial || '—'} · {row.snapshot.systemPositionName || '—'}
                        </div>
                      </div>
                    </Space>
                  ),
                },
                ...(hasParams ? [{
                  title: 'Tiêu chuẩn', key: 'param', width: 170,
                  render: (_: unknown, row: ResultRow) => (
                    <div>
                      <div style={{ fontWeight: 500 }}><ExperimentOutlined style={{ marginRight: 4, color: uiColors.accentPurple }} />{row.param?.paramName}</div>
                      <div style={{ fontSize: 11, color: textColors.secondary }}>Ngưỡng: {formatThreshold(row.param)}</div>
                    </div>
                  ),
                } as never] : []),
                ...(hasParams ? [{
                  title: 'Giá trị đo', key: 'measured', width: 150,
                  render: (_: unknown, row: ResultRow) => {
                    const k = row.key;
                    return editableRows ? (
                      <Input
                        value={drafts[k]?.measuredValue ?? ''}
                        onChange={e => updateDraft(k, { measuredValue: e.target.value })}
                        placeholder={row.param?.unit ? `VD: 45 ${row.param.unit}` : 'VD: 45'}
                      />
                    ) : <span>{drafts[k]?.measuredValue || '—'}</span>;
                  },
                } as never] : []),
                {
                  title: 'Đạt?', key: 'isPass', width: hasParams ? 110 : 100,
                  render: (_: unknown, row: ResultRow) => {
                    const k = row.key;
                    if (hasParams && row.param) {
                      const auto = evaluateIsPass(row.param, drafts[k]?.measuredValue);
                      if (auto === null) return <Tag>Chưa xác định</Tag>;
                      return auto ? <Tag color="success">Đạt</Tag> : <Tag color="error">Không đạt</Tag>;
                    }
                    const val = drafts[k]?.isPass ?? true;
                    return editableRows ? (
                      <Switch
                        checkedChildren="Đạt"
                        unCheckedChildren="KĐạt"
                        checked={val}
                        onChange={v => updateDraft(k, { isPass: v })}
                      />
                    ) : val ? <Tag color="success">Đạt</Tag> : <Tag color="error">Không đạt</Tag>;
                  },
                },
                {
                  title: 'Ghi chú', key: 'notes',
                  render: (_: unknown, row: ResultRow) => {
                    const k = row.key;
                    return editableRows ? (
                      <Input
                        value={drafts[k]?.notes ?? ''}
                        onChange={e => updateDraft(k, { notes: e.target.value })}
                        placeholder="Ghi chú"
                      />
                    ) : <span>{drafts[k]?.notes || '—'}</span>;
                  },
                },
                ...(editableRows ? [{
                  title: 'Lưu', key: 'rowSave', width: 80,
                  render: (_: unknown, row: ResultRow) => {
                    const k = row.key;
                    const dirty = isDirty(row.snapshot.id, it.id, row.paramId, row.param);
                    const saved = savedKeys.has(k);
                    return savingKeys.has(k)
                      ? <Spin size="small" />
                      : saved && !dirty
                        ? <CheckCircleOutlined style={{ color: uiColors.success }} />
                        : <Button size="small" type="link" disabled={!dirty} onClick={() => saveRow(row.snapshot.id, it.id, row.paramId)}>Lưu</Button>;
                  },
                } as never] : []),
              ]}
            />
          )}
        </>
      ),
    };
    });

  return (
    <div>
      <Space style={{ marginBottom: 12 }} wrap>
        <Button icon={<ArrowLeftOutlined />} onClick={() => navigate('/maintenance/campaigns')}>Quay lại</Button>
        <Text strong style={{ fontSize: 16 }}>Đợt bảo dưỡng {campaign.batchNumber || '(không mã)'}</Text>
        <Tag icon={<ClusterOutlined />}>{campaign.systemInfoName}</Tag>
        <Tag color="blue">v{campaign.versionNumber}</Tag>
        {completed
          ? <Tag color="success">Hoàn thành</Tag>
          : <Tag color="processing">Đang thực hiện</Tag>}
      </Space>

      <Card size="small" style={{ marginBottom: 16 }}>
        <Descriptions size="small" column={isMobile ? 1 : 4}>
          <Descriptions.Item label="Bắt đầu">{fmtDate(campaign.startDate)}</Descriptions.Item>
          <Descriptions.Item label="Kết thúc">{fmtDate(campaign.endDate)}</Descriptions.Item>
          <Descriptions.Item label="Người thực hiện">
            {campaign.executors.length > 0
              ? campaign.executors.map(e => <Tag key={e.userId} icon={<UserOutlined />}>{e.fullName}</Tag>)
              : '—'}
          </Descriptions.Item>
          <Descriptions.Item label="Tiến độ kết quả">
            <Tag color={allRecorded ? 'success' : 'warning'}>{recordedTotal} / {applicableTotal}</Tag>
          </Descriptions.Item>
        </Descriptions>

        {canManage && !completed && (
          <Space style={{ marginTop: 12 }} wrap>
            <Tooltip title={checklistError ? 'Không tải được danh mục kiểm tra — không thể hoàn thành.' : !hasChecklist ? 'Đợt chưa có hạng mục checklist — không thể hoàn thành.' : undefined}>
              <span>
                <Button
                  type="primary"
                  icon={<CheckCircleOutlined />}
                  loading={completing}
                  disabled={!hasChecklist || completing}
                  onClick={() => {
                    if (!hasChecklist) return;
                    modal.confirm({
                      title: 'Hoàn thành đợt bảo dưỡng?',
                      content: !allRecorded
                        ? `Còn ${missingResults} hạng mục chưa có kết quả, vẫn hoàn thành?`
                        : 'Sau khi hoàn thành, KHÔNG THỂ sửa kết quả nữa. Hệ thống cũng tính ngày bảo dưỡng tiếp theo (ngày kết thúc + chu kỳ ngắn nhất).',
                      okText: 'Hoàn thành',
                      cancelText: 'Hủy',
                      okButtonProps: { danger: true },
                      onOk: () => doComplete(!allRecorded),
                    });
                  }}
                >
                  Hoàn thành đợt
                </Button>
              </span>
            </Tooltip>
            {!hasChecklist && (
              <Alert
                type="warning"
                showIcon
                style={{ flex: 1, minWidth: 280 }}
                title="Đợt chưa có hạng mục checklist nào — không thể hoàn thành."
              />
            )}
            {hasChecklist && !allRecorded && (
              <Alert
                type="info"
                showIcon
                style={{ flex: 1, minWidth: 280 }}
                title={`Còn ${missingResults} hạng mục chưa có kết quả (${recordedTotal}/${applicableTotal}).`}
              />
            )}
          </Space>
        )}
        {canManage && completed && (
          <Alert
            type="success"
            showIcon
            style={{ marginTop: 12 }}
            title={`Đợt đã hoàn thành — nội dung bất biến.${campaign.endDate ? ` Kết thúc: ${fmtDate(campaign.endDate)}.` : ''}`}
          />
        )}
      </Card>

      <Card size="small" title="Kết quả kiểm tra (theo hạng mục)">
        {checklistError && (
          <Alert
            type={checklistError.kind === 'forbidden' ? 'error' : 'error'}
            showIcon
            style={{ marginBottom: 12 }}
            title={checklistError.kind === 'forbidden' ? 'Không có quyền xem danh mục kiểm tra' : 'Không tải được danh mục kiểm tra'}
            description={checklistError.message}
            action={<Button size="small" onClick={() => { setLoading(true); void loadChecklist().finally(() => setLoading(false)); }}>Thử lại</Button>}
          />
        )}
        {!checklistError && campaign.snapshots.length === 0 && (
          <Alert type="info" showIcon title="Hệ thống không có tài sản nào tại thời điểm tạo đợt — không có kết quả cần ghi." />
        )}
        {!checklistError && items.length === 0 && campaign.snapshots.length > 0 && (
          <Alert type="info" showIcon title="Danh mục kiểm tra của phiên bản không có hạng mục nào." />
        )}
        {collapseItems.length > 0 && (
          <Collapse
            defaultActiveKey={collapseItems.slice(0, 1).map(i => i.key)}
            items={collapseItems}
          />
        )}
      </Card>
    </div>
  );
}