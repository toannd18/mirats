import { useMemo } from 'react';
import { Card, Divider, Empty, Space, Tag, Typography } from 'antd';
import { UserSwitchOutlined } from '@ant-design/icons';
import { ProList, ProTable } from '@ant-design/pro-components';
import type { ActionType, ProColumns } from '@ant-design/pro-components';
import { useIsMobile } from '../../hooks/useIsMobile';
import { uiColors } from '../../theme/designTokens';

const { Text } = Typography;

/**
 * Shared row shape for the audit-trail tables (GET /action-logs and GET /action-logs/by-system).
 * `itemName` is only populated by the /by-system endpoint (Asset display name).
 */
export interface ActionLogRow {
  id: string;
  itemType: string;
  itemId: string;
  actionType: string;
  actionTypeValue: number;
  targetType: string | null;
  targetId: string | null;
  targetName: string | null;
  targetSystemInfoId?: string | null;
  creatorName: string | null;
  note: string | null;
  logMeta: string | null;
  locationName: string | null;
  targetSystemInfoName: string | null;
  actionDate: string;
  itemName?: string | null;
}

export const ACTION_TYPE_TAGS: Record<number, { color: string; label: string }> = {
  1: { color: 'green', label: 'Tạo mới' },
  2: { color: 'blue', label: 'Cập nhật' },
  3: { color: 'red', label: 'Xóa' },
  4: { color: 'orange', label: 'Cấp phát' },
  5: { color: 'purple', label: 'Thu hồi' },
  6: { color: 'cyan', label: 'Kiểm kê' },
  7: { color: 'geekblue', label: 'Import' },
  8: { color: 'magenta', label: 'Export' },
  9: { color: 'green', label: 'Chấp nhận' },
  10: { color: 'red', label: 'Từ chối' },
  11: { color: 'lime', label: 'Xác nhận' },
  12: { color: 'volcano', label: 'Lưu trữ' },
  13: { color: 'gold', label: 'Mở lại' },
  14: { color: 'default', label: 'Cập nhật bị từ chối' },
  15: { color: 'cyan', label: 'Nhập kho' },
  16: { color: 'red', label: 'Đánh dấu hỏng' },
  17: { color: 'volcano', label: 'Loại bỏ' },
};

const CHANGE_LABELS: Record<string, string> = {
  status: 'Trạng thái',
  isConfirmed: 'Xác nhận',
  location_id: 'Vị trí',
  checkout_counter: 'Lần cấp phát',
  checkin_counter: 'Lần thu hồi',
  quantity: 'Số lượng',
  checkout_type: 'Loại cấp phát',
  return_qty: 'Số lượng trả lại',
};

const CHECKOUT_TYPE_LABELS: Record<string, string> = {
  User: 'Người dùng',
  Department: 'Phòng ban',
  Location: 'Vị trí',
  SystemPosition: 'Vị trí hệ thống',
};

/** Formats note + logMeta into a single human-readable "Chi tiết" string. */
export function formatLogDetail(note: string | null, logMeta: string | null): string {
  const parts: string[] = [];
  if (note) parts.push(note);
  if (logMeta) {
    try {
      const meta = JSON.parse(logMeta) as Record<string, unknown>;
      const changes = (meta as { changes?: Record<string, { old?: unknown; new?: unknown }> }).changes;
      if (changes) {
        for (const [k, v] of Object.entries(changes)) {
          // Skip ID fields — show resolved snapshot names instead (LocationName / targetName / TargetSystemInfoName).
          if (k === 'current_assignment_id' || k === 'system_position_id' || k === 'location_id') continue;
          const fmt = (val: unknown) => {
            if (val === null || val === undefined || val === '') return '—';
            if (k === 'status') return ({ Pending: 'Chờ cấp phát', Deployed: 'Đã cấp phát', Archived: 'Đã lưu trữ' })[String(val)] ?? String(val);
            if (k === 'isConfirmed') return /true/i.test(String(val)) ? 'Đã xác nhận' : 'Chưa xác nhận';
            if (k === 'checkout_type') return CHECKOUT_TYPE_LABELS[String(val)] ?? String(val);
            return String(val);
          };
          const oldStr = fmt(v?.old);
          const newStr = fmt(v?.new);
          if (oldStr === newStr) continue;
          const label = CHANGE_LABELS[k] ?? k.replace(/_/g, ' ');
          if (oldStr === '—' && newStr !== '—') parts.push(`${label}: ${newStr}`);
          else if (newStr === '—' && oldStr !== '—') parts.push(`${label}: đã gỡ bỏ`);
          else parts.push(`${label}: ${oldStr} → ${newStr}`);
        }
      } else {
        // Top-level metadata (e.g. component logs: { quantity, serialNo, before, after }).
        for (const [k, v] of Object.entries(meta)) {
          if (v === null || v === undefined || v === '') continue;
          parts.push(`${k}: ${String(v)}`);
        }
      }
    } catch { /* ignore malformed meta */ }
  }
  return parts.join(' · ') || '—';
}

function baseColumns(targetColumnTitle: string): ProColumns<ActionLogRow>[] {
  return [
    { title: 'Thời gian', dataIndex: 'actionDate', key: 'actionDate', valueType: 'dateTime', width: 160 },
    {
      title: 'Hành động', dataIndex: 'actionTypeValue', key: 'actionTypeValue', width: 130,
      render: (_, record) => {
        const info = ACTION_TYPE_TAGS[record.actionTypeValue] ?? { color: 'default', label: record.actionType };
        return <Tag color={info.color}>{info.label}</Tag>;
      },
    },
    {
      title: 'Người thực hiện', dataIndex: 'creatorName', key: 'creatorName', width: 180, ellipsis: true,
      render: (_, record) => (
        <Space size={4}><UserSwitchOutlined style={{ color: uiColors.labelGray }} /><Text>{record.creatorName || '-'}</Text></Space>
      ),
    },
    {
      title: targetColumnTitle, dataIndex: 'targetName', key: 'targetName', width: 200, ellipsis: true,
      render: (_, record) => record.targetName || '-',
    },
    {
      title: 'Chi tiết', dataIndex: 'note', key: 'detail', width: 320, ellipsis: true,
      render: (_, record) => {
        const parts: string[] = [];
        if (record.locationName) parts.push(`Vị trí: ${record.locationName}`);
        if (record.targetSystemInfoName) parts.push(`Hệ thống: ${record.targetSystemInfoName}`);
        const metaDetail = formatLogDetail(null, record.logMeta);
        if (metaDetail !== '—') parts.push(metaDetail);
        if (record.note) parts.push(record.note);
        return parts.join(' · ') || '—';
      },
    },
  ];
}

export interface ActionLogRequestParams {
  current?: number;
  pageSize?: number;
  [key: string]: unknown;
}

export interface ActionLogRequestResult {
  data: ActionLogRow[];
  success: boolean;
  total: number;
}

type ActionLogRequest = (params: ActionLogRequestParams, sort: unknown, filter: unknown) => Promise<ActionLogRequestResult>;

interface ActionLogTableProps {
  headerTitle?: React.ReactNode;
  /** Title of the target column — e.g. "Đối tượng liên quan" or "Vị trí lắp đặt". */
  targetColumnTitle?: string;
  /** Columns appended after the base history columns (e.g. "Tài sản"). */
  extraColumns?: ProColumns<ActionLogRow>[];
  request?: ActionLogRequest;
  params?: Record<string, unknown>;
  actionRef?: React.Ref<ActionType> | undefined;
  pagination?: false | { pageSize?: number; showSizeChanger?: boolean };
  emptyText?: React.ReactNode;
}

/**
 * Reusable, read-only audit-trail table (ProTable). Both the Asset detail history and the
 * "Lịch sử hệ thống" page render through this component — no parallel copies of the logic.
 */
const ActionLogTable: React.FC<ActionLogTableProps> = ({
  headerTitle,
  targetColumnTitle = 'Đối tượng liên quan',
  extraColumns = [],
  request,
  params,
  actionRef,
  pagination = { pageSize: 10, showSizeChanger: false },
  emptyText = 'Chưa có lịch sử',
}) => {
  const isMobile = useIsMobile();
  const columns = useMemo(
    () => [...baseColumns(targetColumnTitle), ...extraColumns],
    [targetColumnTitle, extraColumns],
  );

  // Total fixed width of all columns → drives the horizontal scroll width. A fixed
  // table-layout + numeric scroll-x is what actually enforces per-column width with
  // ellipsis: with scroll x:'max-content', antd lets the widest cell expand the table
  // and the declared column width is ignored (the "Chi tiết" cell grew to 1001px).
  const totalWidth = useMemo(
    () => columns.reduce((sum, col) => sum + (typeof col.width === 'number' ? col.width : 0), 0),
    [columns],
  );

  // ─── Mobile (T-RESP4, ST7b): Card list thay Table — dùng chung request/params của caller.
  // Áp dụng TẠI ĐÂY (component dùng chung) nên MỌI Detail page nhúng ActionLogTable đều
  // responsive mà không cần sửa từng trang (Asset/Component/System/SystemHistory).
  if (isMobile) {
    return (
      <ProList<ActionLogRow>
        rowKey="id"
        actionRef={actionRef}
        ghost
        cardProps={false}
        search={false}
        grid={{ gutter: 12, xs: 1, sm: 1 }}
        headerTitle={headerTitle}
        request={request as never}
        params={params}
        pagination={{ pageSize: pagination === false ? 10 : (pagination.pageSize ?? 10), showSizeChanger: false }}
        locale={{ emptyText: <Empty description={emptyText} image={Empty.PRESENTED_IMAGE_SIMPLE} /> }}
        itemRender={(record) => {
          const info = ACTION_TYPE_TAGS[record.actionTypeValue] ?? { color: 'default', label: record.actionType };
          const parts: string[] = [];
          if (record.locationName) parts.push(`Vị trí: ${record.locationName}`);
          if (record.targetSystemInfoName) parts.push(`Hệ thống: ${record.targetSystemInfoName}`);
          const metaDetail = formatLogDetail(null, record.logMeta);
          if (metaDetail !== '—') parts.push(metaDetail);
          if (record.note) parts.push(record.note);
          const detail = parts.join(' · ') || '—';
          return (
            <Card size="small" style={{ borderRadius: 10, marginBottom: 12 }}>
              <div style={{ display: 'flex', alignItems: 'center', gap: 8, marginBottom: 8, flexWrap: 'wrap' }}>
                <Tag color={info.color} style={{ marginInlineEnd: 0 }}>{info.label}</Tag>
                <Text type="secondary" style={{ fontSize: 12 }}>
                  {new Date(record.actionDate).toLocaleString('vi-VN')}
                </Text>
              </div>
              {record.itemName && (
                <Text strong style={{ fontSize: 14, display: 'block', marginBottom: 6 }}>{record.itemName}</Text>
              )}
              <div style={{ display: 'grid', gridTemplateColumns: 'auto 1fr', gap: '4px 10px' }}>
                <Text type="secondary" style={{ fontSize: 12 }}>Người thực hiện</Text>
                <Text style={{ fontSize: 13 }}>{record.creatorName || '-'}</Text>
                {record.targetName && (
                  <>
                    <Text type="secondary" style={{ fontSize: 12 }}>{targetColumnTitle}</Text>
                    <Text style={{ fontSize: 13, wordBreak: 'break-word' }}>{record.targetName}</Text>
                  </>
                )}
              </div>
              {detail !== '—' && (
                <>
                  <Divider style={{ margin: '8px 0' }} />
                  <Text style={{ fontSize: 12, whiteSpace: 'pre-wrap', wordBreak: 'break-word' }}>{detail}</Text>
                </>
              )}
            </Card>
          );
        }}
      />
    );
  }

  return (
    <ProTable<ActionLogRow>
      rowKey="id"
      headerTitle={headerTitle}
      actionRef={actionRef}
      columns={columns}
      search={false}
      options={false}
      toolBarRender={false}
      scroll={{ x: totalWidth || 'max-content' }}
      tableLayout="fixed"
      pagination={pagination}
      params={params}
      request={request}
      locale={{ emptyText: <Empty description={emptyText} image={Empty.PRESENTED_IMAGE_SIMPLE} /> }}
    />
  );
};

export default ActionLogTable;
