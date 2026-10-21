import { useMemo } from 'react';
import { Card, Divider, Empty, Space, Tag, Typography } from 'antd';
import { UserSwitchOutlined } from '@ant-design/icons';
import { ProList, ProTable } from '@ant-design/pro-components';
import type { ActionType, ProColumns } from '@ant-design/pro-components';
import { useIsMobile } from '../../hooks/useIsMobile';
import { uiColors } from '../../theme/designTokens';
import { formatDate, formatDateTime } from '../../utils/format';

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
  18: { color: 'default', label: 'Đóng' },
  19: { color: 'gold', label: 'Mở lại' },
  20: { color: 'cyan', label: 'Kiểm tra' },
  21: { color: 'blue', label: 'Công bố' },
  22: { color: 'green', label: 'Hoàn thành' },
  23: { color: 'purple', label: 'Đính chính' },
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
  // [AUDIT-LOG B6a] Nhật ký User: đọc được CẢ HAI kiểu khóa — camelCase (log CŨ trước B6a) và
  // snake_case (log MỚI). Thiếu map ⇒ cột "Chi tiết" hiện tên khóa thô tiếng Anh.
  firstName: 'Tên',
  first_name: 'Tên',
  lastName: 'Họ',
  last_name: 'Họ',
  username: 'Tên đăng nhập',
  email: 'Email',
  employeeNumber: 'Mã nhân viên',
  employee_number: 'Mã nhân viên',
  // [AUDIT-LOG B7a] log nhập kho serial: meta phẳng { serialNo, trackingType }
  serialNo: 'Serial',
  trackingType: 'Kiểu quản lý',
  jobTitle: 'Chức danh',
  job_title: 'Chức danh',
  isActive: 'Đang hoạt động',
  is_active: 'Đang hoạt động',
  isSuperUser: 'Quyền superuser',
  is_super_user: 'Quyền superuser',
  companyId: 'Công ty',
  company_id: 'Công ty',
  departmentId: 'Phòng ban',
  department_id: 'Phòng ban',
  locationId: 'Vị trí',
  // [AUDIT-LOG GĐ2] Nhãn cho các field của command đã gộp về helper (key snake_case).
  name: 'Tên',
  code: 'Mã',
  asset_tag: 'Mã tài sản',
  serial: 'Serial',
  notes: 'Ghi chú',
  description: 'Mô tả',
  phone: 'Điện thoại',
  fax: 'Fax',
  address: 'Địa chỉ',
  city: 'Thành phố',
  state: 'Tỉnh/Bang',
  country: 'Quốc gia',
  zip: 'Mã bưu chính',
  parent_id: 'Đơn vị cha',
  manager_id: 'Người phụ trách',
  tag_color: 'Màu thẻ',
  checkin_email: 'Email khi thu hồi',
  require_acceptance: 'Yêu cầu chấp nhận',
  use_default_eula: 'Dùng EULA mặc định',
  field_values: 'Giá trị',
  field_encrypted: 'Mã hoá',
  help_text: 'Trợ giúp',
  is_unique: 'Duy nhất',
  model_number: 'Model',
  order_number: 'Số đơn hàng',
  purchase_cost: 'Giá mua',
  purchase_date: 'Ngày mua',
  warranty_months: 'Bảo hành (tháng)',
  min_amt: 'Tồn tối thiểu',
  item_no: 'Mã vật tư',
  category_id: 'Danh mục',
  manufacturer_id: 'Nhà sản xuất',
  supplier_id: 'Nhà cung cấp',
  system_info_id: 'Hệ thống',
  effective_from: 'Hiệu lực từ',
  param_name: 'Tên thông số',
  measured_value: 'Giá trị đo',
  is_pass: 'Đạt',
  end_date: 'Ngày kết thúc',
  reviewer_id: 'Người kiểm tra',
  next_maintenance_due_date: 'Bảo dưỡng kế tiếp',
  group_ids: 'Nhóm người dùng',
  permissions: 'Quyền',
  format: 'Định dạng mã',
  enabled: 'Bật',
  item_name: 'Tên mục',
  min_qty: 'Định mức',
  note: 'Ghi chú',
  scope: 'Phạm vi',
  version: 'Phiên bản',
  version_id: 'Phiên bản',
  versionId: 'Phiên bản',
  template_id: 'Mẫu bảo dưỡng',
  templateId: 'Mẫu bảo dưỡng',
  template_version_id: 'Phiên bản mẫu',
  templateVersionId: 'Phiên bản mẫu',
  version_number: 'Số phiên bản',
  versionNumber: 'Số phiên bản',
  snapshot_count: 'Số thiết bị',
  snapshotCount: 'Số thiết bị',
  batch_number: 'Mã đợt',
  batchNumber: 'Mã đợt',
  start_date: 'Ngày bắt đầu',
  startDate: 'Ngày bắt đầu',
  endDate: 'Ngày kết thúc',
  reviewerId: 'Người kiểm tra',
  executor_ids: 'Người thực hiện',
  executorIds: 'Người thực hiện',
  is_current: 'Hiện hành',
  isCurrent: 'Hiện hành',
  published_at: 'Công bố lúc',
  publishedAt: 'Công bố lúc',
  effectiveFrom: 'Hiệu lực từ',
};

const CHECKOUT_TYPE_LABELS: Record<string, string> = {
  User: 'Người dùng',
  Department: 'Phòng ban',
  Location: 'Vị trí',
  SystemPosition: 'Vị trí hệ thống',
};

/** Keys whose value is a calendar date — render the date only, never a time. */
const DATE_ONLY_KEYS = new Set([
  'start_date', 'end_date', 'purchase_date', 'expiration_date', 'termination_date',
  'next_maintenance_due_date', 'startDate', 'endDate', 'purchaseDate', 'expirationDate',
  'terminationDate', 'nextMaintenanceDueDate',
]);

/** Any key ending in `_date`/`Date` is a calendar date, including future fields. */
const isDateOnlyKey = (key: string) => DATE_ONLY_KEYS.has(key)
  || /(?:^|_)date$/i.test(key) || /Date$/.test(key);

const SCOPE_LABELS: Record<string, string> = {
  version: 'Phiên bản',
  item: 'Mục',
  param: 'Tham số',
};

/** Keys whose value is a full timestamp — render date + time. */
const DATE_TIME_KEYS = new Set([
  'published_at', 'effective_from', 'publishedAt', 'effectiveFrom', 'created_at', 'updated_at',
  'createdAt', 'updatedAt',
]);

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
            if (typeof val === 'string'
                && /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(val)) return '—';
            if (typeof val === 'string' && isDateOnlyKey(k)) return formatDate(val);
            if (typeof val === 'string' && DATE_TIME_KEYS.has(k)) return formatDateTime(val);
            if (typeof val === 'string' && /^\d{4}-\d{2}-\d{2}T/.test(val)) return formatDateTime(val);
            if (typeof val === 'string' && /^\d{4}-\d{2}-\d{2}$/.test(val)) return formatDate(val);
            // [AUDIT-LOG B7a] `status` dùng chung cho NHIỀU enum khác nhau: AssetStatus
            // (Pending/Deployed/Archived) và ComponentUnitStatus (InStock/Allocated/Damaged/Disposed —
            // log cấp phát/thu hồi serial + đổi trạng thái unit). Hai tập giá trị không trùng nhau.
            if (k === 'status') return ({
              Pending: 'Chờ cấp phát', Deployed: 'Đã cấp phát', Archived: 'Đã lưu trữ',
              InStock: 'Trong kho', Allocated: 'Đang cấp phát', Damaged: 'Hỏng', Disposed: 'Đã loại bỏ',
            })[String(val)] ?? String(val);
            if (k === 'isConfirmed') return /true/i.test(String(val)) ? 'Đã xác nhận' : 'Chưa xác nhận';
            if (k === 'checkout_type') return CHECKOUT_TYPE_LABELS[String(val)] ?? String(val);
            if (k === 'scope') return SCOPE_LABELS[String(val)] ?? String(val);
            // Cờ boolean (is_active / is_super_user / isActive / isSuperUser…) hiển thị
            // tiếng Việt thay vì "True"/"False" thô — áp dụng cho cả log cũ và mới.
            if (typeof val === 'boolean') return val ? 'Có' : 'Không';
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
        // Top-level metadata (e.g. component logs: { quantity, serialNo, before, after }) hoặc nhật ký
        // User CŨ do backend ghi kiểu phẳng (không có wrapper `changes`: { username, email, isActive… }).
        // [AUDIT-LOG B6a] Vẫn áp NHÃN tiếng Việt + boolean cho cả hai kiểu khóa (camelCase/snake_case).
        for (const [k, v] of Object.entries(meta)) {
          if (v === null || v === undefined || v === '') continue;
          if (typeof v === 'object') continue; // object lồng: không in "[object Object]"
          let val = typeof v === 'boolean' ? (v ? 'Có' : 'Không') : String(v);
          if (k === 'scope') val = SCOPE_LABELS[val] ?? val;
          if (/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(val)) continue;
          if (isDateOnlyKey(k)) val = formatDate(val);
          else if (DATE_TIME_KEYS.has(k)) val = formatDateTime(val);
          else if (/^\d{4}-\d{2}-\d{2}T/.test(val)) val = formatDateTime(val);
          else if (/^\d{4}-\d{2}-\d{2}$/.test(val)) val = formatDate(val);
          parts.push(`${CHANGE_LABELS[k] ?? k}: ${val}`);
        }
      }
    } catch { /* ignore malformed meta */ }
  }
  return parts.join(' · ') || '—';
}

function baseColumns(targetColumnTitle: string): ProColumns<ActionLogRow>[] {
  return [
    { title: 'Thời gian', dataIndex: 'actionDate', key: 'actionDate', width: 160,
      render: (_, record) => formatDateTime(record.actionDate) },
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
  pagination?: false | { pageSize?: number; showSizeChanger?: boolean };  emptyText?: React.ReactNode;
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

  const paginationConfig = pagination === false ? false : {
    ...pagination,
    pageSize: pagination.pageSize ?? 10,
    showTotal: (total: number, range: [number, number]) => `${range[0]}–${range[1]} / ${total} bản ghi`,
  };

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
        pagination={{ pageSize: pagination === false ? 10 : (pagination.pageSize ?? 10), showSizeChanger: false,
          showTotal: (total, range) => `${range[0]}–${range[1]} / ${total} bản ghi` }}
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
                  {formatDateTime(record.actionDate)}
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
      pagination={paginationConfig}
      params={params}
      request={request}
      locale={{ emptyText: <Empty description={emptyText} image={Empty.PRESENTED_IMAGE_SIMPLE} /> }}
    />
  );
};

export default ActionLogTable;
