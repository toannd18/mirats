import { useRef, useState } from 'react';
import {
  App, Button, Card, Descriptions, Divider, Empty, Space, Tag, Tooltip, Typography,
} from 'antd';
import type { MenuProps } from 'antd';
import {
  CalendarOutlined, CheckCircleOutlined, CheckOutlined, ClusterOutlined, CloseOutlined, DeleteOutlined,
  DollarOutlined, EditOutlined, EnvironmentOutlined, LockOutlined, SafetyCertificateOutlined,
  ShopOutlined, SyncOutlined, TeamOutlined, UnlockOutlined,
} from '@ant-design/icons';
import { ProList, ProTable } from '@ant-design/pro-components';
import type { ActionType, ProColumns } from '@ant-design/pro-components';
import { Link } from 'react-router-dom';
import apiClient from '../../../services/api-client';
import { assetService, type AssetMaintenanceDto } from '../../asset/services/asset.service';
import { isSuperUser } from '../../../features/auth/services/auth';
import { usePermission } from '../../../hooks/usePermission';
import { useIsMobile } from '../../../hooks/useIsMobile';
import MaintenanceCompleteModal from './MaintenanceCompleteModal';
import ListRowActions from '../../../shared/components/list/ListRowActions';
import type { ListPrimaryAction } from '../../../shared/components/list/ListRowActions';
import { EllipsisText, NowrapText } from '../../../shared/components/list/ListText';
import { useListColumnsState } from '../../../shared/hooks/useListColumnsState';
import { statusColors, uiColors, cardBadgeGradients } from '../../../theme/designTokens';
import ModalFormShell from '../../../shared/components/ModalFormShell';
import { formatDate, formatDateTime, formatMoney } from '../../../utils/format';

const { Text, Title } = Typography;

export const MAINTENANCE_TYPE_LABELS: Record<number, string> = {
  1: 'Bảo trì định kỳ',
  2: 'Sửa chữa',
  3: 'Nâng cấp',
  4: 'Hỗ trợ phần cứng',
  5: 'Hỗ trợ phần mềm',
  6: 'PAT Test',
  7: 'Hiệu chuẩn',
  8: 'Báo cáo sự cố',
};

export const MAINTENANCE_TYPE_COLORS: Record<number, string> = {
  1: 'blue', 2: 'orange', 3: 'geekblue', 4: 'cyan',
  5: 'magenta', 6: 'purple', 7: 'gold', 8: 'red',
};

export const MAINTENANCE_TYPE_OPTIONS = Object.entries(MAINTENANCE_TYPE_LABELS)
  .map(([value, label]) => ({ value: Number(value), label }));

// Backend serializes the enum as its NAME (e.g. "Repair") — map name → numeric value.
export const MAINTENANCE_TYPE_VALUE: Record<string, number> = {
  Maintenance: 1,
  Repair: 2,
  Upgrade: 3,
  HardwareSupport: 4,
  SoftwareSupport: 5,
  PatTest: 6,
  Calibration: 7,
  IncidentReport: 8,
};

// Compares context by ID (not display name) — the Snapshot field gets an orange "Đã thay đổi" marker
// when the LIVE context differs from what was captured at maintenance-creation time.
export function maintenanceContextChanged(snapshotId: string | null, currentId: string | null | undefined): boolean {
  return !!currentId && currentId !== snapshotId;
}

// ─── Maintenance STATUS (computed — backend KHÔNG có enum MaintenanceStatus) ───
// Trạng thái được SUY RA từ completionDate + isClosed + inspectedById
// (workflow entity: Hoàn thành → Kiểm tra → Đóng). Bảng màu dưới đây TÁI SỬ DỤNG 100%
// mapping đã có sẵn trong codebase (MaintenanceTable cột "Trạng thái" cũ:
// success/processing/default; AssetMaintenanceSection: green/processing/default +
// Đã kiểm tra) — KHÔNG bịa bảng màu mới. Lưu ý: "Quá hạn" KHÔNG hiển thị được vì
// model không có field ngày dự kiến/planned end date.
export type MaintenanceStatusKey = 'in_progress' | 'completed' | 'closed';

/** Màu trạng thái bảo trì — NGUỒN DUY NHẤT từ `statusColors` (theme/designTokens).
 * Dùng CHUNG cho cả Tag trạng thái VÀ icon/badge trên card (trước đây Tag dùng preset
 * AntD success/processing/default còn badge dùng hex chép tay → 2 sắc xanh khác nhau
 * cho cùng 1 trạng thái vì designTokens override colorSuccess/colorInfo). */
export const MAINTENANCE_STATUS_COLORS: Record<MaintenanceStatusKey, string> = {
  in_progress: statusColors.ready,   // #1677ff
  completed: statusColors.active,     // #52c41a
  closed: statusColors.closed,        // #8c8c8c
};

export function getMaintenanceStatus(r: { isClosed: boolean; completionDate?: string | null }): MaintenanceStatusKey {
  if (r.isClosed) return 'closed';
  if (r.completionDate) return 'completed';
  return 'in_progress';
}

// Nhãn trạng thái tiếng Việt dùng chung cho Tag (bảng desktop + card mobile).
const MAINTENANCE_STATUS_LABELS: Record<MaintenanceStatusKey, string> = {
  in_progress: 'Đang thực hiện',
  completed: 'Hoàn thành',
  closed: 'Đã đóng',
};

// ─── Column state (UI-1 pattern): 6 cột mặc định + 7 cột tuỳ chọn mặc định ẩn ───
// v2: bỏ sorter client (chỉ còn sorter server-side) + thêm cột tuỳ chọn có nguồn dữ liệu thật
// (inspected / createdAt / closedAt) → bump key để user cũ nhận lại layout mới.
const MAINTENANCE_COLUMN_DEFAULTS: Record<string, { show: boolean; fixed?: 'left' | 'right' }> = {
  asset: { show: true },
  type: { show: true },
  title: { show: true },
  startDate: { show: true },
  completionDate: { show: true },
  cost: { show: true },
  supplier: { show: false },
  assignees: { show: false },
  isWarranty: { show: false },
  inspected: { show: false },
  createdAt: { show: false },
  closedAt: { show: false },
  notes: { show: false },
  actions: { show: true, fixed: 'right' },
};

const OPTIONAL_COLUMN_KEYS = ['supplier', 'assignees', 'isWarranty', 'inspected', 'createdAt', 'closedAt', 'notes'];
const COLUMNS_STORAGE_KEY = 'maintenance-list-columns-v2';

/** Whitelist sort server-side — PHẢI khớp MaintenanceListSort ở backend (ListAllMaintenancesQuery.cs).
 *  Cột không có trong danh sách này không được bật `sorter` (tránh sort client / sort rỗng). */
const SORTABLE_COLUMN_KEYS = new Set(['title', 'startDate', 'completionDate', 'cost', 'createdAt']);

// ==================== Styles (đồng bộ AccessoryListPage/ComponentListPage/LicenseListPage) ====================

const iconBadgeStyle: React.CSSProperties = {
  width: 48,
  height: 48,
  borderRadius: 12,
  background: cardBadgeGradients.blue,
  display: 'flex',
  alignItems: 'center',
  justifyContent: 'center',
  flexShrink: 0,
};

const dataGridStyle: React.CSSProperties = {
  display: 'grid',
  gridTemplateColumns: '1fr 1fr',
  gap: '8px 16px',
  padding: '12px 16px',
  background: '#fafafa',
  borderRadius: 8,
  border: '1px solid #f0f0f0',
};

const dataRowStyle: React.CSSProperties = {
  display: 'flex',
  alignItems: 'center',
  gap: 6,
};

const labelIconStyle: React.CSSProperties = {
  color: uiColors.labelGray,
  fontSize: 13,
};

// ─── Status-color helpers cho icon/badge card (pattern LicenseListPage, nhưng màu theo
// TRẠNG THÁI bảo trì thay vì category.tagColor — Maintenance không có category). ───

function hexToRgba(hex: string, alpha: number): string {
  const clean = hex.trim().replace(/^#/, '');
  const full = clean.length === 3
    ? clean.split('').map((c) => c + c).join('')
    : clean;
  // Defensive: non-hex/empty input must not produce NaN — fall back to processing blue.
  if (full.length !== 6 || !/^[0-9a-fA-F]{6}$/.test(full)) {
    return `rgba(22, 119, 255, ${alpha})`;
  }
  const num = parseInt(full, 16);
  const r = (num >> 16) & 255;
  const g = (num >> 8) & 255;
  const b = num & 255;
  return `rgba(${r}, ${g}, ${b}, ${alpha})`;
}

function badgeBackground(color: string): string {
  return `linear-gradient(135deg, ${hexToRgba(color, 0.16)} 0%, ${hexToRgba(color, 0.32)} 100%)`;
}

function MaintenanceStatusBadgeIcon({ status }: { status: MaintenanceStatusKey }) {
  if (status === 'closed') return <LockOutlined style={{ fontSize: 22, color: MAINTENANCE_STATUS_COLORS.closed }} />;
  if (status === 'completed') return <CheckOutlined style={{ fontSize: 22, color: MAINTENANCE_STATUS_COLORS.completed }} />;
  return <SyncOutlined style={{ fontSize: 22, color: MAINTENANCE_STATUS_COLORS.in_progress }} />;
}

interface MaintenanceTableProps {
  /** When set, the table is scoped to maintenances recorded for assets of this system (SnapshotSystemInfoId). */
  systemInfoId?: string;
  actionRef?: React.MutableRefObject<ActionType | undefined>;
  /** Optional toolbar node (e.g. a "Thêm bảo trì" button owned by the parent page). */
  createButton?: React.ReactNode;
}

/** Tham số tìm kiếm server-side mà GET /maintenances hỗ trợ (assetId + systemInfoId). */
type MaintenanceSearchParams = { assetId?: string };

/**
 * Reusable Asset Maintenance list (UI-1 approved pattern: ProTable desktop + ProList Card mobile,
 * one state-driven primary action + ⋯ menu, detail modal).
 * Used by /maintenances (all records) and by the SystemDetailPage Maintenance tab (systemInfoId scope).
 */
export default function MaintenanceTable({ systemInfoId, actionRef, createButton }: MaintenanceTableProps) {
  const { message, modal } = App.useApp();
  const isMobile = useIsMobile();
  const superUser = isSuperUser();
  const canDeleteMaintenance = usePermission('assets.edit');
  const canEditMaintenance = usePermission('assets.edit');
  const internalActionRef = useRef<ActionType | undefined>(undefined);
  const ref = actionRef ?? internalActionRef;
  const [detail, setDetail] = useState<AssetMaintenanceDto | null>(null);
  const [detailOpen, setDetailOpen] = useState(false);
  const [closedByUserName, setClosedByUserName] = useState<string | null>(null);
  // Task H — modal "Hoàn thành bảo trì" (mở TẠI CHỖ bằng state cục bộ, không navigate).
  const [completeTarget, setCompleteTarget] = useState<AssetMaintenanceDto | null>(null);

  // Cột tuỳ chọn (Nhà cung cấp/Người thực hiện/Bảo hành/Ghi chú) đang hiện hay không — quyết định
  // cột "Thao tác" có cần ghim phải hay không (pattern AssetListPage).
  const [optionalColumnsVisible, setOptionalColumnsVisible] = useState(() => {
    try {
      const raw = localStorage.getItem(COLUMNS_STORAGE_KEY);
      if (!raw) return false;
      const state = JSON.parse(raw) as Record<string, { show?: boolean }>;
      return OPTIONAL_COLUMN_KEYS.some((key) => state[key]?.show === true);
    } catch {
      return false;
    }
  });

  const columnsState = useListColumnsState(COLUMNS_STORAGE_KEY, MAINTENANCE_COLUMN_DEFAULTS);

  // One shared fetch path for both views (ProTable request + mobile card list) — no duplication.
  // Server hỗ trợ page/pageSize/assetId/systemInfoId + whitelist sort (sortBy/sortDir).
  // Sorting is SERVER-SIDE ONLY: không có hàm sorter client nào trong file này.
  const fetchPage = async (opts: {
    page: number;
    pageSize: number;
    assetId?: string;
    sortBy?: string;
    sortDir?: 'asc' | 'desc';
  }) => {
    const res = await assetService.listAllMaintenances({
      page: opts.page,
      pageSize: opts.pageSize,
      ...(systemInfoId ? { systemInfoId } : {}),
      ...(opts.assetId ? { assetId: opts.assetId } : {}),
      ...(opts.sortBy ? { sortBy: opts.sortBy, sortDir: opts.sortDir ?? 'asc' } : {}),
    });
    return {
      data: (res.data.data ?? []) as AssetMaintenanceDto[],
      total: res.data.pagination?.totalItems ?? 0,
    };
  };

  /** ProTable sort map → { sortBy, sortDir } chỉ khi cột nằm trong whitelist server-side. */
  const toServerSort = (sort?: Record<string, 'ascend' | 'descend' | null>) => {
    const entry = Object.entries(sort ?? {})
      .find(([key, order]) => SORTABLE_COLUMN_KEYS.has(key) && (order === 'ascend' || order === 'descend'));
    if (!entry) return {};
    return { sortBy: entry[0], sortDir: (entry[1] === 'descend' ? 'desc' : 'asc') as 'asc' | 'desc' };
  };

  const reload = () => ref.current?.reload();

  // Detail comes from GET /maintenances/{id} (list rows do not carry currentContext).
  const handleDetail = async (r: AssetMaintenanceDto) => {
    try {
      const res = await assetService.getMaintenance(r.id);
      const d = (res.data?.data ?? r) as AssetMaintenanceDto;
      setDetail(d);
      if (d.isClosed && d.closedById) {
        try {
          const u = await apiClient.get(`/users/${d.closedById}`);
          const name = u.data?.data?.username ?? u.data?.data?.firstName;
          setClosedByUserName(typeof name === 'string' ? name : null);
        } catch {
          setClosedByUserName(null);
        }
      } else {
        setClosedByUserName(null);
      }
    } catch {
      setDetail(r);
      setClosedByUserName(null);
    }
    setDetailOpen(true);
  };

  const handleClose = (r: AssetMaintenanceDto) => {
    modal.confirm({
      title: 'Đóng bản ghi bảo trì này?',
      content: 'Sau khi đóng, bản ghi sẽ bị khóa và không thể chỉnh sửa (khóa audit).',
      okText: 'Đóng',
      cancelText: 'Hủy',
      onOk: async () => {
        try {
          await assetService.closeMaintenance(r.id);
          void message.success('Đã đóng bản ghi bảo trì (khóa mọi chỉnh sửa)');
          reload();
        } catch (err: unknown) {
          const e = err as { response?: { data?: { message?: string } } };
          void message.error(e?.response?.data?.message || 'Không thể đóng bản ghi bảo trì');
          throw new Error('close failed');
        }
      },
    });
  };

  const handleInspect = async (r: AssetMaintenanceDto) => {
    try {
      await assetService.inspectMaintenance(r.id);
      void message.success('Đã đánh dấu đã kiểm tra');
      // Refresh the detail modal if it's showing this record so the inspected state updates in place.
      if (detail && detail.id === r.id) await handleDetail(r);
      reload();
    } catch (err: unknown) {
      const e = err as { response?: { data?: { message?: string } } };
      void message.error(e?.response?.data?.message || 'Không thể đánh dấu đã kiểm tra');
    }
  };

  const handleReopen = (r: AssetMaintenanceDto) => {
    modal.confirm({
      title: 'Mở lại bản ghi bảo trì?',
      content: 'Hành động này phá bỏ khóa audit — bản ghi sẽ có thể chỉnh sửa lại. Chỉ Superuser được phép và thao tác sẽ được ghi vào nhật ký (ActionLog).',
      okText: 'Mở lại',
      okButtonProps: { danger: true },
      cancelText: 'Hủy',
      onOk: async () => {
        try {
          await assetService.reopenMaintenance(r.id);
          void message.success('Đã mở lại bản ghi bảo trì');
          reload();
        } catch {
          void message.error('Không thể mở lại bản ghi bảo trì');
          throw new Error('reopen failed');
        }
      },
    });
  };

  const handleDelete = (r: AssetMaintenanceDto) => {
    modal.confirm({
      title: 'Xóa bản ghi bảo trì này?',
      content: 'Hành động này không thể hoàn tác.',
      okText: 'Xóa',
      cancelText: 'Hủy',
      okButtonProps: { danger: true },
      onOk: async () => {
        try {
          await assetService.deleteMaintenance(r.id);
          void message.success('Đã xóa bản ghi bảo trì');
          reload();
        } catch {
          void message.error('Không thể xóa bản ghi bảo trì');
          throw new Error('delete failed');
        }
      },
    });
  };

  /**
   * UI-1: đúng MỘT hành động chính theo trạng thái (nút outlined cố định 104px).
   * Đang thực hiện → Hoàn thành · Đã hoàn thành (chưa kiểm tra) → Kiểm tra ·
   * Đã kiểm tra (chưa đóng) → Đóng · Đã đóng → không có.
   */
  const primaryActionFor = (record: AssetMaintenanceDto): ListPrimaryAction | null => {
    if (record.isClosed) return null;
    if (!record.completionDate) {
      return canEditMaintenance
        ? { key: 'complete', label: 'Hoàn thành', icon: <CheckCircleOutlined />, onClick: () => setCompleteTarget(record) }
        : null;
    }
    if (!record.inspectedById) {
      return canEditMaintenance
        ? { key: 'inspect', label: 'Kiểm tra', icon: <CheckOutlined />, onClick: () => void handleInspect(record) }
        : null;
    }
    return canEditMaintenance
      ? { key: 'close', label: 'Đóng', icon: <LockOutlined />, onClick: () => handleClose(record) }
      : null;
  };

  /**
   * Các hành động còn lại vào menu ⋯ — CHỈ: Sửa (khi có dữ liệu hoàn thành để sửa),
   * Mở lại (Superuser, bản ghi đã đóng), Xóa. TUYỆT ĐỐI không lặp lại hành động chính
   * (primaryKey bị loại khỏi menu, kể cả khi sau này key đổi trùng).
   */
  const secondaryMenuItems = (record: AssetMaintenanceDto, primaryKey?: string): MenuProps['items'] => {
    const items: NonNullable<MenuProps['items']> = [];
    const add = (key: string, item: NonNullable<MenuProps['items']>[number]) => {
      if (key !== primaryKey && item) items.push(item);
    };
    // Chỉ bản ghi ĐÃ hoàn thành mới có nhóm dữ liệu hoàn thành để sửa (modal Hoàn thành bảo trì).
    if (!record.isClosed && record.completionDate && canEditMaintenance) {
      add('edit', {
        key: 'edit',
        label: 'Sửa',
        icon: <EditOutlined />,
        onClick: () => setCompleteTarget(record),
      });
    }
    if (record.isClosed && superUser) {
      add('reopen', {
        key: 'reopen',
        label: 'Mở lại',
        icon: <UnlockOutlined />,
        onClick: () => handleReopen(record),
      });
    }
    if (canDeleteMaintenance) {
      add('delete', {
        key: 'delete',
        label: 'Xóa',
        icon: <DeleteOutlined />,
        danger: true,
        onClick: () => handleDelete(record),
      });
    }
    return items;
  };

  const renderRowActions = (record: AssetMaintenanceDto, stopPropagation = false) => {
    const primary = primaryActionFor(record);
    return (
      <ListRowActions
        primary={primary}
        menuItems={secondaryMenuItems(record, primary?.key)}
        stopPropagation={stopPropagation}
      />
    );
  };

  // ─── Desktop columns: 6 cột mặc định + 4 cột tuỳ chọn (mặc định ẩn) ───
  const desktopColumns: ProColumns<AssetMaintenanceDto>[] = ([
    {
      title: 'Tài sản', key: 'asset', width: 240,
      render: (_, record) => {
        const asset = record.asset;
        if (!asset) return <Text type="secondary">-</Text>;
        return (
          <div style={{ display: 'flex', alignItems: 'center', gap: 6, minWidth: 0 }}>
            <div style={{ minWidth: 0, flex: 1 }}>
              <Tooltip title={asset.name} placement="topLeft">
                <Link
                  to={`/assets/${asset.id}`}
                  onClick={(e) => e.stopPropagation()}
                  style={{ display: 'block', overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}
                >
                  {asset.name}
                </Link>
              </Tooltip>
            </div>
            <NowrapText value={asset.assetTag} code />
          </div>
        );
      },
    },
    {
      title: 'Loại', key: 'type', width: 110,
      render: (_, record) => {
        const typeValue = MAINTENANCE_TYPE_VALUE[record.type] ?? 1;
        return (
          <Tag color={MAINTENANCE_TYPE_COLORS[typeValue]} style={{ borderRadius: 4, margin: 0 }}>
            {MAINTENANCE_TYPE_LABELS[typeValue]}
          </Tag>
        );
      },
    },
    {
      title: 'Tiêu đề', dataIndex: 'title', key: 'title', width: 180,
      // Sort server-side (whitelist: title) — không có hàm sorter client.
      sorter: true,
      render: (_, record) => <EllipsisText value={record.title} />,
    },
    {
      title: 'Bắt đầu', dataIndex: 'startDate', key: 'startDate', width: 95,
      sorter: true,
      render: (_, record) => <NowrapText value={formatDate(record.startDate)} />,
    },
    {
      title: 'Hoàn thành', dataIndex: 'completionDate', key: 'completionDate', width: 130,
      sorter: true,
      render: (_, record) => {
        if (!record.completionDate) return <Text type="secondary">{MAINTENANCE_STATUS_LABELS.in_progress}</Text>;
        return (
          <Space size={4} wrap>
            <NowrapText value={formatDate(record.completionDate)} />
            {record.inspectedById && (
              <Tooltip title={record.inspectedAt ? `Đã kiểm tra ${formatDateTime(record.inspectedAt)}` : 'Đã kiểm tra'}>
                <CheckOutlined style={{ color: MAINTENANCE_STATUS_COLORS.completed }} />
              </Tooltip>
            )}
            {record.isClosed && (
              <Tag color={MAINTENANCE_STATUS_COLORS.closed} icon={<LockOutlined />} style={{ borderRadius: 4, margin: 0 }}>
                {MAINTENANCE_STATUS_LABELS.closed}
              </Tag>
            )}
          </Space>
        );
      },
    },
    {
      title: 'Chi phí', dataIndex: 'cost', key: 'cost', width: 110, align: 'right',
      sorter: true,
      render: (_, record) => <NowrapText value={formatMoney(record.cost)} />,
    },
    {
      title: 'Nhà cung cấp', key: 'supplier', width: 180,
      render: (_, record) => <EllipsisText value={record.supplier?.name} />,
    },
    {
      title: 'Người thực hiện', key: 'assignees', width: 200,
      render: (_, record) => (
        <EllipsisText value={(record.assignees ?? []).map((a) => a.name).join(', ') || null} />
      ),
    },
    {
      title: 'Bảo hành', key: 'isWarranty', width: 110,
      render: (_, record) => (
        record.isWarranty
          ? <Tag color="green" style={{ margin: 0 }}>Có</Tag>
          : <Tag style={{ margin: 0 }}>Không</Tag>
      ),
    },
    {
      // Nguồn dữ liệu: inspectedAt/inspectedByName (có sẵn trong AllMaintenanceListItemDto).
      title: 'Đã kiểm tra', key: 'inspected', width: 190,
      render: (_, record) => (record.inspectedById
        ? (
          <Space size={4} wrap>
            <CheckOutlined style={{ color: MAINTENANCE_STATUS_COLORS.completed }} />
            <NowrapText value={record.inspectedByName || 'Đã kiểm tra'} />
            <Text type="secondary" style={{ fontSize: 12 }}>{formatDateTime(record.inspectedAt)}</Text>
          </Space>
        )
        : <Text type="secondary">Chưa kiểm tra</Text>),
    },
    {
      // Nguồn dữ liệu: createdAt (luôn có) — cột mới thay cho việc "sort ngầm" trước đây.
      title: 'Ngày tạo', dataIndex: 'createdAt', key: 'createdAt', width: 150,
      sorter: true,
      render: (_, record) => <NowrapText value={formatDateTime(record.createdAt)} />,
    },
    {
      // Nguồn dữ liệu: closedAt — vừa được thêm vào AllMaintenanceListItemDto (trước đây list
      // không trả field này nên cột "Ngày đóng" sẽ luôn '-').
      title: 'Ngày đóng', dataIndex: 'closedAt', key: 'closedAt', width: 150,
      render: (_, record) => (record.isClosed && record.closedAt
        ? <NowrapText value={formatDateTime(record.closedAt)} />
        : <Text type="secondary">-</Text>),
    },
    {
      title: 'Ghi chú', key: 'notes', width: 240,
      render: (_, record) => <EllipsisText value={record.notes} />,
    },
    ...(systemInfoId ? [{
      title: 'Vị trí hệ thống', key: 'systemPosition', width: 180,
      render: (_: unknown, record: AssetMaintenanceDto) => <EllipsisText value={record.snapshotSystemPositionName} />,
    } as ProColumns<AssetMaintenanceDto>] : []),
    {
      title: 'Thao tác', key: 'actions', width: 150,
      fixed: optionalColumnsVisible ? undefined : 'right',
      render: (_: unknown, record: AssetMaintenanceDto) => renderRowActions(record, true),
    },
  ] as ProColumns<AssetMaintenanceDto>[]).map((column) => ({ ...column, hideInSearch: true, search: false }));

  // ─── Search columns: GET /maintenances chỉ hỗ trợ assetId (các filter khác chưa có ở server) ───
  const searchColumns: ProColumns<AssetMaintenanceDto>[] = [
    {
      title: 'Tài sản',
      dataIndex: 'assetId',
      valueType: 'select',
      hideInTable: true,
      fieldProps: { showSearch: true, placeholder: 'Tìm tên hoặc mã tài sản' },
      request: async () => {
        try {
          const res = await assetService.list({ page: 1, pageSize: 500 });
          const list = (res.data?.data ?? []) as { id: string; assetTag: string; name: string }[];
          return list.map((a) => ({ label: `${a.name} (${a.assetTag})`, value: a.id }));
        } catch {
          return [];
        }
      },
    },
  ];

  const headerTitle = !systemInfoId
    ? <Title level={4} style={{ margin: 0 }}>Bảo trì tài sản</Title>
    : undefined;

  const emptyText = <Empty description="Không có bảo trì" />;

  // Trạng thái tags trên card — tái sử dụng NGUYÊN VẸN mapping ProTable cũ
  // (success/processing/default + icons Check/Close/Lock). LƯU Ý: kiểm tra record.completionDate
  // TRỰC TIẾP (giống ProTable cũ), KHÔNG dùng getMaintenanceStatus() ở đây — bản ghi ĐÃ ĐÓNG
  // luôn có completionDate (backend bắt buộc), nên phải hiện "Hoàn thành" + "Đã đóng",
  // không được hiện "Đang thực hiện".
  const renderStatusTags = (record: AssetMaintenanceDto) => (
    <Space size={[4, 4]} wrap>
      {record.completionDate
        ? <Tag color={MAINTENANCE_STATUS_COLORS.completed} icon={<CheckOutlined />} style={{ borderRadius: 4, margin: 0 }}>Hoàn thành</Tag>
        : <Tag color={MAINTENANCE_STATUS_COLORS.in_progress} icon={<CloseOutlined />} style={{ borderRadius: 4, margin: 0 }}>Đang thực hiện</Tag>}
      {record.isClosed && (
        <Tag color={MAINTENANCE_STATUS_COLORS.closed} icon={<LockOutlined />} style={{ borderRadius: 4, margin: 0 }}>Đã đóng</Tag>
      )}
    </Space>
  );

  return (
    <>
      {isMobile ? (
        <ProList<AssetMaintenanceDto, MaintenanceSearchParams>
          headerTitle={headerTitle}
          actionRef={ref}
          rowKey="id"
          ghost
          cardProps={false}
          columns={searchColumns}
          // Mobile: bộ lọc thu gọn mặc định để card chiếm trọn màn hình.
          search={systemInfoId ? false : { labelWidth: 'auto', defaultCollapsed: true }}
          locale={{ emptyText }}
          grid={{ gutter: 16, xs: 1, sm: 1, md: 2, lg: 2, xl: 3, xxl: 3 }}
          toolBarRender={() => (createButton ? [createButton] : [])}
          request={async (params) => {
            try {
              const { current, pageSize, assetId } = params;
              const { data, total } = await fetchPage({
                page: current ?? 1,
                pageSize: pageSize ?? 12,
                assetId,
              });
              return { data, total, success: true };
            } catch {
              void message.error('Lỗi tải danh sách bảo trì');
              return { data: [], total: 0, success: false };
            }
          }}
          pagination={{
            defaultPageSize: 12,
            showSizeChanger: true,
            showTotal: (total, range) => `${range[0]}-${range[1]} / ${total} mục`,
          }}
          itemRender={(record) => {
            const status = getMaintenanceStatus(record);
            const statusColor = MAINTENANCE_STATUS_COLORS[status];
            const asset = record.asset;
            const typeValue = MAINTENANCE_TYPE_VALUE[record.type] ?? 1;
            return (
              <Card
                hoverable
                onClick={() => void handleDetail(record)}
                style={{
                  borderRadius: 12,
                  marginBottom: 16,
                  cursor: 'pointer',
                  transition: 'all 0.25s cubic-bezier(0.4, 0, 0.2, 1)',
                }}
                styles={{ body: { padding: '20px 20px 16px' } }}
              >
                {/* ── Header: icon badge màu theo TRẠNG THÁI + tiêu đề + loại ── */}
                <div style={{ display: 'flex', alignItems: 'center', gap: 12, marginBottom: 12 }}>
                  <div style={{ ...iconBadgeStyle, background: badgeBackground(statusColor) }}>
                    <MaintenanceStatusBadgeIcon status={status} />
                  </div>
                  <div style={{ flex: 1, minWidth: 0 }}>
                    <div style={{ display: 'flex', alignItems: 'baseline', gap: 8, flexWrap: 'wrap' }}>
                      <Text strong style={{ fontSize: 16, lineHeight: 1.4 }}>
                        {record.title || 'Không tiêu đề'}
                      </Text>
                      <Tag color={MAINTENANCE_TYPE_COLORS[typeValue]} style={{ borderRadius: 4, margin: 0 }}>
                        {MAINTENANCE_TYPE_LABELS[typeValue]}
                      </Tag>
                    </div>
                    <div style={{ marginTop: 4 }}>
                      {asset ? (
                        <Text type="secondary" style={{ fontSize: 13 }}>
                          {/* T-UX1: Link trong card phải stopPropagation để không mở nhầm detail modal */}
                          <Link to={`/assets/${asset.id}`} onClick={(e) => e.stopPropagation()}>
                            {asset.name} ({asset.assetTag})
                          </Link>
                        </Text>
                      ) : (
                        <Text type="secondary" style={{ fontSize: 13 }}>—</Text>
                      )}
                    </div>
                  </div>
                </div>

                {/* ── Status tags ── */}
                <div style={{ marginBottom: 12, paddingLeft: 60 }}>{renderStatusTags(record)}</div>

                {/* ── Data grid (không có Ghi chú trên card mobile) ── */}
                <div style={dataGridStyle}>
                  <div style={dataRowStyle}>
                    <CalendarOutlined style={labelIconStyle} />
                    <Text type="secondary" style={{ fontSize: 12 }}>Ngày bắt đầu</Text>
                  </div>
                  <div style={dataRowStyle}>
                    <Text strong style={{ fontSize: 13 }}>{formatDate(record.startDate)}</Text>
                  </div>

                  <div style={dataRowStyle}>
                    <CalendarOutlined style={labelIconStyle} />
                    <Text type="secondary" style={{ fontSize: 12 }}>Ngày hoàn thành</Text>
                  </div>
                  <div style={dataRowStyle}>
                    <Text strong style={{ fontSize: 13 }}>{formatDate(record.completionDate)}</Text>
                  </div>

                  <div style={dataRowStyle}>
                    <TeamOutlined style={labelIconStyle} />
                    <Text type="secondary" style={{ fontSize: 12 }}>Người phụ trách</Text>
                  </div>
                  <div style={dataRowStyle}>
                    {(record.assignees?.length ?? 0) > 0 ? (
                      <Text strong style={{ fontSize: 13 }}>{record.assignees!.map(a => a.name).join(', ')}</Text>
                    ) : (
                      <Text type="secondary" italic style={{ fontSize: 12 }}>Chưa phân công</Text>
                    )}
                  </div>

                  <div style={dataRowStyle}>
                    <ShopOutlined style={labelIconStyle} />
                    <Text type="secondary" style={{ fontSize: 12 }}>NCC</Text>
                  </div>
                  <div style={dataRowStyle}>
                    <Text strong style={{ fontSize: 13 }}>{record.supplier?.name ?? '-'}</Text>
                  </div>

                  <div style={dataRowStyle}>
                    <DollarOutlined style={labelIconStyle} />
                    <Text type="secondary" style={{ fontSize: 12 }}>Chi phí</Text>
                  </div>
                  <div style={dataRowStyle}>
                    <Text strong style={{ fontSize: 13 }}>{formatMoney(record.cost)}</Text>
                  </div>

                  <div style={dataRowStyle}>
                    <SafetyCertificateOutlined style={labelIconStyle} />
                    <Text type="secondary" style={{ fontSize: 12 }}>Bảo hành</Text>
                  </div>
                  <div style={dataRowStyle}>
                    {record.isWarranty
                      ? <Tag color="green" style={{ margin: 0 }}>Có</Tag>
                      : <Tag style={{ margin: 0 }}>Không</Tag>}
                  </div>

                  {systemInfoId ? (
                    <>
                      <div style={dataRowStyle}>
                        <ClusterOutlined style={labelIconStyle} />
                        <Text type="secondary" style={{ fontSize: 12 }}>Vị trí trong hệ thống</Text>
                      </div>
                      <div style={dataRowStyle}>
                        <Text strong style={{ fontSize: 13 }}>{record.snapshotSystemPositionName || '—'}</Text>
                      </div>
                    </>
                  ) : null}

                  {superUser ? (
                    <>
                      <div style={dataRowStyle}>
                        <EnvironmentOutlined style={labelIconStyle} />
                        <Text type="secondary" style={{ fontSize: 12 }}>Công ty</Text>
                      </div>
                      <div style={dataRowStyle}>
                        {record.asset?.companyName
                          ? <Text strong style={{ fontSize: 13 }}>{record.asset.companyName}</Text>
                          : <Tag style={{ margin: 0 }}>Chưa gán</Tag>}
                      </div>
                    </>
                  ) : null}
                </div>

                {/* ── Divider + Actions (1 hành động chính theo trạng thái + menu ⋯) ── */}
                <Divider style={{ margin: '12px 0' }} />
                <div style={{ display: 'flex', justifyContent: 'flex-end' }}>
                  {renderRowActions(record, true)}
                </div>
              </Card>
            );
          }}
        />
      ) : (
        <ProTable<AssetMaintenanceDto, MaintenanceSearchParams>
          headerTitle={headerTitle}
          actionRef={ref}
          rowKey="id"
          columns={[...searchColumns, ...desktopColumns]}
          // Desktop: bộ lọc mở sẵn (tab trong SystemDetail không cần filter tài sản).
          search={systemInfoId ? false : { labelWidth: 'auto', defaultCollapsed: false }}
          options={{ reload: true, density: true, setting: true }}
          columnsState={{
            ...columnsState,
            onChange: (map) => {
              const wide = OPTIONAL_COLUMN_KEYS.some((key) => map[key]?.show === true);
              setTimeout(() => setOptionalColumnsVisible(wide), 0);
            },
          }}
          locale={{ emptyText }}
          toolBarRender={() => (createButton ? [createButton] : [])}
          onRow={(record) => ({
            onClick: () => { void handleDetail(record); },
            style: { cursor: 'pointer' },
          })}
          request={async (params, sort) => {
            try {
              const { current, pageSize, assetId } = params;
              const { data, total } = await fetchPage({
                page: current ?? 1,
                pageSize: pageSize ?? 12,
                assetId,
                ...toServerSort(sort),
              });
              return { data, total, success: true };
            } catch {
              void message.error('Lỗi tải danh sách bảo trì');
              return { data: [], total: 0, success: false };
            }
          }}
          pagination={{
            defaultPageSize: 12,
            showSizeChanger: true,
            showTotal: (total, range) => `${range[0]}-${range[1]} / ${total} mục`,
          }}
          scroll={{ x: 'max-content' }}
        />
      )}

      {/* ─── Detail modal (snapshot block) ─── */}
      <ModalFormShell
        title="Chi tiết bảo trì"
        open={detailOpen}
        onCancel={() => setDetailOpen(false)}
        footer={null}
        size="md"
      >
        {detail && (
          <>
            <Descriptions column={2} size="small" bordered>
              <Descriptions.Item label="Tài sản" span={2}>
                {detail.asset ? `${detail.asset.name} (${detail.asset.assetTag})` : '-'}
              </Descriptions.Item>
              <Descriptions.Item label="Loại">
                <Tag color={MAINTENANCE_TYPE_COLORS[MAINTENANCE_TYPE_VALUE[detail.type] ?? 1]}>
                  {MAINTENANCE_TYPE_LABELS[MAINTENANCE_TYPE_VALUE[detail.type] ?? 1]}
                </Tag>
              </Descriptions.Item>
              <Descriptions.Item label="Trạng thái">
                <Space size={4}>
                  {detail.completionDate ? 'Hoàn thành' : 'Đang thực hiện'}
                  {detail.isClosed && <Tag color="default" icon={<LockOutlined />}>Đã đóng</Tag>}
                </Space>
              </Descriptions.Item>
              <Descriptions.Item label="Tiêu đề" span={2}>{detail.title}</Descriptions.Item>
              <Descriptions.Item label="Ghi chú" span={2}>{detail.notes || '-'}</Descriptions.Item>
              <Descriptions.Item label="Nhà cung cấp">{detail.supplier?.name ?? '-'}</Descriptions.Item>
              <Descriptions.Item label="Bảo hành">{detail.isWarranty ? 'Có' : 'Không'}</Descriptions.Item>
              <Descriptions.Item label="Ngày bắt đầu">{formatDate(detail.startDate)}</Descriptions.Item>
              <Descriptions.Item label="Ngày hoàn thành">{formatDate(detail.completionDate)}</Descriptions.Item>
              <Descriptions.Item label="Chi phí">{formatMoney(detail.cost)}</Descriptions.Item>
              <Descriptions.Item label="Người phụ trách" span={2}>
                {(detail.assignees?.length ?? 0) > 0
                  ? (
                    <Space size={[4, 4]} wrap>
                      {detail.assignees!.map(a => (
                        <Tag key={a.userId} color="blue" style={{ marginInlineEnd: 0 }}>{a.name}</Tag>
                      ))}
                    </Space>
                  )
                  : <Text type="secondary">Chưa phân công</Text>}
              </Descriptions.Item>
              <Descriptions.Item label="Đã kiểm tra" span={2}>
                {detail.inspectedById
                  ? (
                    <span>
                      <CheckOutlined style={{ color: statusColors.active }} />{' '}
                      {detail.inspectedByName || 'Đã kiểm tra'} lúc{' '}
                      {formatDateTime(detail.inspectedAt)}
                    </span>
                  )
                  : detail.completionDate
                    ? (
                      <Button size="small" icon={<CheckOutlined />} onClick={() => void handleInspect(detail)}>
                        Đánh dấu đã kiểm tra
                      </Button>
                    )
                    : (
                      <Tooltip title="Cần nhập Ngày hoàn thành trước khi kiểm tra bảo trì">
                        <Button size="small" disabled icon={<CheckOutlined />}>Đánh dấu đã kiểm tra</Button>
                      </Tooltip>
                    )}
              </Descriptions.Item>


              {detail.isClosed && (
                <Descriptions.Item label="Đã đóng" span={2}>
                  <Tag color="default" icon={<LockOutlined />}>Đã đóng</Tag>{' '}
                  lúc {formatDateTime(detail.closedAt)}
                  {closedByUserName
                    ? <> bởi <Text strong>{closedByUserName}</Text></>
                    : detail.closedById
                      ? <Text type="secondary"> (id: {detail.closedById.slice(0, 8)}…)</Text>
                      : null}
                </Descriptions.Item>
              )}
            </Descriptions>
            <Divider titlePlacement="start" plain>Ngữ cảnh tại thời điểm bảo trì (ảnh chụp nhanh)</Divider>
            <Descriptions column={2} size="small" bordered>
              <Descriptions.Item label="Hệ thống">
                {detail.snapshotSystemInfoName || '-'}
                {maintenanceContextChanged(detail.snapshotSystemInfoId, detail.currentContext?.systemInfoId)
                  && <Tag color="orange" style={{ marginLeft: 8 }}>Đã thay đổi</Tag>}
              </Descriptions.Item>
              <Descriptions.Item label="Vị trí trong hệ thống">
                {detail.snapshotSystemPositionName || '-'}
                {maintenanceContextChanged(detail.snapshotSystemPositionId, detail.currentContext?.systemPositionId)
                  && <Tag color="orange" style={{ marginLeft: 8 }}>Đã thay đổi</Tag>}
              </Descriptions.Item>
              <Descriptions.Item label="Vị trí">
                {detail.snapshotLocationName || '-'}
                {maintenanceContextChanged(detail.snapshotLocationId, detail.currentContext?.locationId)
                  && <Tag color="orange" style={{ marginLeft: 8 }}>Đã thay đổi</Tag>}
              </Descriptions.Item>
              <Descriptions.Item label="Người phụ trách">
                {detail.snapshotAssignedUserName || '-'}
                {maintenanceContextChanged(detail.snapshotAssignedUserId, detail.currentContext?.assignedUserId)
                  && <Tag color="orange" style={{ marginLeft: 8 }}>Đã thay đổi</Tag>}
              </Descriptions.Item>
              <Descriptions.Item label="Phòng ban" span={2}>
                {detail.snapshotDepartmentName || '-'}
                {maintenanceContextChanged(detail.snapshotDepartmentId, detail.currentContext?.departmentId)
                  && <Tag color="orange" style={{ marginLeft: 8 }}>Đã thay đổi</Tag>}
              </Descriptions.Item>
            </Descriptions>
            <Divider titlePlacement="start" plain>Ngữ cảnh hiện tại (dữ liệu sống)</Divider>
            <Descriptions column={2} size="small" bordered>
              <Descriptions.Item label="Hệ thống">{detail.currentContext?.systemInfoName || '-'}</Descriptions.Item>
              <Descriptions.Item label="Vị trí trong hệ thống">{detail.currentContext?.systemPositionName || '-'}</Descriptions.Item>
              <Descriptions.Item label="Vị trí">{detail.currentContext?.locationName || '-'}</Descriptions.Item>
              <Descriptions.Item label="Người phụ trách">{detail.currentContext?.assignedUserName || '-'}</Descriptions.Item>
              <Descriptions.Item label="Phòng ban" span={2}>{detail.currentContext?.departmentName || '-'}</Descriptions.Item>
            </Descriptions>
          </>
        )}
      </ModalFormShell>

      {/* ─── Task H: modal "Hoàn thành bảo trì" — mở tại chỗ, list tự reload sau khi lưu ─── */}
      <MaintenanceCompleteModal
        record={completeTarget}
        onClose={() => setCompleteTarget(null)}
        onSaved={() => {
          setCompleteTarget(null);
          reload();
        }}
      />
    </>
  );
}
