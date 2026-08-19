import { useRef, useState, type ReactNode } from 'react';
import {
  App, Button, Card, Descriptions, Divider, Empty, Modal, Popconfirm, Space, Tag, Tooltip, Typography,
} from 'antd';
import {
  CalendarOutlined, CheckCircleOutlined, CheckOutlined, CloseOutlined, ClusterOutlined, DeleteOutlined, DollarOutlined,
  EditOutlined, EnvironmentOutlined, EyeOutlined, LockOutlined, SafetyCertificateOutlined, ShopOutlined,
  SyncOutlined, TeamOutlined, UnlockOutlined,
} from '@ant-design/icons';
import { ProList } from '@ant-design/pro-components';
import type { ActionType } from '@ant-design/pro-components';
import { Link, useNavigate } from 'react-router-dom';
import apiClient from '../../../services/api-client';
import { assetService, type AssetMaintenanceDto } from '../../asset/services/asset.service';
import { isSuperUser } from '../../../services/keycloak';
import { usePermission } from '../../../hooks/usePermission';
import MaintenanceCompleteModal from './MaintenanceCompleteModal';
import { statusColors } from '../../../theme/designTokens';
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

// ==================== Styles (đồng bộ AccessoryListPage/ComponentListPage/LicenseListPage) ====================

const iconBadgeStyle: React.CSSProperties = {
  width: 48,
  height: 48,
  borderRadius: 12,
  background: 'linear-gradient(135deg, #f0f5ff 0%, #adc6ff 100%)',
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
  color: '#8c8c8c',
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

/**
 * Reusable Asset Maintenance table (ProTable + detail modal + close/reopen/delete actions).
 * Used by /maintenances (all records) and by the SystemDetailPage Maintenance tab (systemInfoId scope).
 */
export default function MaintenanceTable({ systemInfoId, actionRef, createButton }: MaintenanceTableProps) {
  const { message, modal } = App.useApp();
  const navigate = useNavigate();
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

  // One shared fetch path for both views (ProTable request + mobile Card list) — no duplication.
  const fetchPage = async (page: number, pageSize: number) => {
    const res = await assetService.listAllMaintenances({
      page,
      pageSize,
      ...(systemInfoId ? { systemInfoId } : {}),
    });
    return {
      data: (res.data.data ?? []) as AssetMaintenanceDto[],
      total: res.data.pagination?.totalItems ?? 0,
    };
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

  const handleClose = async (r: AssetMaintenanceDto) => {
    try {
      await assetService.closeMaintenance(r.id);
      message.success('Đã đóng bản ghi bảo trì (khóa mọi chỉnh sửa)');
      reload();
    } catch (err: unknown) {
      const e = err as { response?: { data?: { message?: string } } };
      message.error(e?.response?.data?.message || 'Không thể đóng bản ghi bảo trì');
    }
  };

  const handleInspect = async (r: AssetMaintenanceDto) => {
    try {
      await assetService.inspectMaintenance(r.id);
      message.success('Đã đánh dấu đã kiểm tra');
      // Refresh the detail modal if it's showing this record so the inspected state updates in place.
      if (detail && detail.id === r.id) await handleDetail(r);
      reload();
    } catch (err: unknown) {
      const e = err as { response?: { data?: { message?: string } } };
      message.error(e?.response?.data?.message || 'Không thể đánh dấu đã kiểm tra');
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
          message.success('Đã mở lại bản ghi bảo trì');
          reload();
        } catch {
          message.error('Không thể mở lại bản ghi bảo trì');
        }
      },
    });
  };

  const handleDelete = async (r: AssetMaintenanceDto) => {
    try {
      await assetService.deleteMaintenance(r.id);
      message.success('Đã xóa bản ghi bảo trì');
      reload();
    } catch {
      message.error('Không thể xóa bản ghi bảo trì');
    }
  };

  // ST7b — action buttons shared by the desktop "Thao tác" column and the mobile Card list
  // (keeps permission-gating and handlers in ONE place — no duplication between views).
  const renderActions = (record: AssetMaintenanceDto): ReactNode[] => [
    <Button key="detail" size="small" icon={<EyeOutlined />} onClick={() => void handleDetail(record)}>
      Chi tiết
    </Button>,
    <Button key="asset" size="small" icon={<EditOutlined />} onClick={() => record.asset && navigate(`/assets/${record.asset.id}`)}>
      Mở tài sản
    </Button>,
    // Task H — đường tắt "Hoàn thành bảo trì" ngay tại Card cho bản ghi ĐANG THỰC HIỆN
    // (thiếu completionDate). Mở modal chuyên biệt tại chỗ — KHÔNG navigate sang AssetDetail.
    // Gate cùng quyền với nút "Sửa" (assets.edit).
    ...(!record.isClosed && !record.completionDate && canEditMaintenance
      ? [
          <Button
            key="complete"
            size="small"
            icon={<CheckCircleOutlined />}
            onClick={() => setCompleteTarget(record)}
          >
            Hoàn thành
          </Button>,
        ]
      : []),
    ...(!record.isClosed && record.completionDate
      ? [
          record.inspectedById
            ? (
              <Tag key="inspected" color="green" icon={<CheckOutlined />}>Đã kiểm tra</Tag>
            )
            : (
              canEditMaintenance && (
                <Button key="inspect" size="small" icon={<CheckOutlined />} onClick={() => void handleInspect(record)}>
                  Đánh dấu đã kiểm tra
                </Button>
              )
            ),
        ]
      : []),
    ...(!record.isClosed && canEditMaintenance
      ? [
          record.completionDate && record.inspectedById
            ? (
              <Popconfirm
                key="close"
                title="Đóng bản ghi bảo trì này?"
                description="Sau khi đóng, bản ghi sẽ bị khóa và không thể chỉnh sửa (khóa audit)."
                onConfirm={() => void handleClose(record)}
              >
                <Button size="small" icon={<LockOutlined />}>Xác nhận đóng</Button>
              </Popconfirm>
            )
            : (
              <Tooltip key="close" title={record.completionDate ? 'Cần kiểm tra trước khi đóng bảo trì' : 'Cần nhập Ngày hoàn thành trước khi đóng bảo trì'}>
                <Button size="small" disabled icon={<LockOutlined />}>Xác nhận đóng</Button>
              </Tooltip>
            ),
        ]
      : []),
    ...(superUser && record.isClosed
      ? [
          <Button key="reopen" size="small" icon={<UnlockOutlined />} onClick={() => void handleReopen(record)}>
            Mở lại
          </Button>,
        ]
      : []),
    ...(canDeleteMaintenance
      ? [(
          <Popconfirm key="del" title="Xóa bản ghi bảo trì này?" onConfirm={() => void handleDelete(record)}>
            <Button size="small" danger icon={<DeleteOutlined />}>Xóa</Button>
          </Popconfirm>
        )]
      : []),
  ];

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
      <ProList<AssetMaintenanceDto>
        headerTitle={!systemInfoId ? <Title level={4} style={{ margin: 0 }}>Bảo trì tài sản</Title> : undefined}
        actionRef={ref}
        rowKey="id"
        ghost
        cardProps={false}
        search={false}
        locale={{ emptyText: <Empty description="Không có bảo trì" /> }}
        grid={{ gutter: 16, xs: 1, sm: 1, md: 2, lg: 2, xl: 3, xxl: 3 }}
        toolBarRender={() => (createButton ? [createButton] : [])}
        request={async (params) => {
          try {
            const { current, pageSize } = params;
            const { data, total } = await fetchPage(current ?? 1, pageSize ?? 10);
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
              style={{
                borderRadius: 12,
                marginBottom: 16,
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
                        <Link to={`/assets/${asset.id}`}>
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

              {/* ── Data grid ── */}
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

              {/* ── Divider + Actions ── */}
              <Divider style={{ margin: '12px 0' }} />
              <Space size="small" wrap style={{ justifyContent: 'flex-end', width: '100%' }}>
                {renderActions(record)}
              </Space>
            </Card>
          );
        }}
      />

      {/* ─── Detail modal (snapshot block) ─── */}
      <Modal
        title="Chi tiết bảo trì"
        open={detailOpen}
        onCancel={() => setDetailOpen(false)}
        footer={null}
        width={640}
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
      </Modal>

      {/* ─── Task H: modal "Hoàn thành bảo trì" — mở tại chỗ, Card tự reload sau khi lưu ─── */}
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
