import { useEffect, useRef, useState } from 'react';
import {
  Alert, App, Button, Card, Col, DatePicker, Descriptions, Divider, Form, Input, Modal, Row, Select, Space, Spin, Tag, Typography,
} from 'antd';
import type { MenuProps } from 'antd';
import {
  CheckCircleOutlined, EditOutlined, PlusOutlined, SafetyCertificateOutlined, UserOutlined,
} from '@ant-design/icons';
import { ProList, ProTable } from '@ant-design/pro-components';
import type { ActionType, ProColumns } from '@ant-design/pro-components';
import { useNavigate } from 'react-router-dom';
import dayjs, { Dayjs } from 'dayjs';
import apiClient from '../../../services/api-client';
import { usePermission } from '../../../hooks/usePermission';
import { useIsMobile } from '../../../hooks/useIsMobile';
import { formatDate } from '../../../utils/format';
import ListRowActions, { type ListPrimaryAction } from '../../../shared/components/list/ListRowActions';
import { EllipsisText } from '../../../shared/components/list/ListText';
import { useListColumnsState } from '../../../shared/hooks/useListColumnsState';

const { Text } = Typography;

interface CampaignDto {
  id: string;
  systemInfoId: string;
  systemInfoName: string;
  templateVersionId: string;
  /** Tên template (entity-backed: TemplateVersion.Template.Name). */
  templateName?: string | null;
  versionNumber: number;
  startDate?: string | null;
  endDate?: string | null;
  batchNumber?: string | null;
  companyId?: string | null;
  reviewerId?: string | null;
  /** Tên người kiểm tra (entity-backed: Reviewer.FirstName/LastName → username). */
  reviewerName?: string | null;
  status: string;
  createdAt: string;
  snapshotCount: number;
  resultsCount: number;
  /** Tên người thực hiện (entity-backed: Executors → User). */
  executorNames?: string[] | null;
}

interface SystemOption { id: string; code: string; name: string; }

interface UserOption { label: string; value: string; }

interface CreateFormValues {
  systemInfoId?: string;
  startDate?: Dayjs;
  batchNumber?: string;
  executorIds?: string[];
  reviewerId?: string;
}

const STATUS_TAG: Record<string, { color: string; label: string }> = {
  Draft: { color: 'default', label: 'Nháp' },
  InProgress: { color: 'processing', label: 'Đang thực hiện' },
  Completed: { color: 'success', label: 'Hoàn thành' },
  Closed: { color: 'default', label: 'Đã đóng' },
};

/** Sort do BACKEND quyết định (whitelist sortBy/sortDir) — không còn comparator phía client. */

/**
 * Whitelist sort của BE (chỉ field DB-backed) — map key cột FE → `sortBy` API.
 * Key không có trong map = cột không sort được (không gửi sortBy, không sort client).
 */
const SORT_FIELD_MAP: Record<string, string> = {
  batch: 'batchnumber',
  batchnumber: 'batchnumber',
  startdate: 'startdate',
  enddate: 'enddate',
  status: 'status',
  versionnumber: 'versionnumber',
};

const resolveSortParams = (sort?: Record<string, unknown>): { sortBy?: string; sortDir?: string } => {
  const entry = Object.entries(sort ?? {})
    .find(([, order]) => order === 'ascend' || order === 'descend');
  if (!entry) return {};
  const sortBy = SORT_FIELD_MAP[String(entry[0]).toLowerCase()];
  if (!sortBy) return {};
  return { sortBy, sortDir: entry[1] === 'descend' ? 'desc' : 'asc' };
};

// Versioned column state — bump key khi đổi bộ cột mặc định.
const CAMPAIGN_COLUMNS_STATE = {
  batch: { show: true },
  systemInfoName: { show: true },
  templateName: { show: true },
  versionNumber: { show: true },
  status: { show: true },
  startDate: { show: true },
  endDate: { show: true },
  reviewerName: { show: false },
  executorNames: { show: false },
  actions: { show: true, fixed: 'right' },
};

/**
 * MC-6 — Danh sách đợt bảo dưỡng (Campaign) + tạo mới. Create chọn SystemInfo → tự hiện
 * TemplateVersion hiện hành sẽ ghim + số Asset sẽ được snapshot (Phần II phiếu gốc),
 * chọn nhiều người thực hiện + 1 người kiểm tra (Executor UI hoãn từ MC-3).
 */
export default function MaintenanceCampaignListPage() {
  const { message, modal } = App.useApp();
  const navigate = useNavigate();
  // [FE-R2/FE-R4] Branch desktop Table ↔ mobile Card theo hook dùng chung (T-RESP1).
  const isMobile = useIsMobile();
  const actionRef = useRef<ActionType | null>(null);
  const canView = usePermission('maintenance.view');
  const canManage = usePermission('maintenance.campaigns');

  const [createOpen, setCreateOpen] = useState(false);
  const [submitting, setSubmitting] = useState(false);
  const [form] = Form.useForm<CreateFormValues>();

  const [systems, setSystems] = useState<SystemOption[]>([]);
  const [systemsLoading, setSystemsLoading] = useState(false);

  // Phần II preview: số asset sẽ được snapshot khi chọn hệ thống.
  const [selectedSystemId, setSelectedSystemId] = useState<string | undefined>();
  const [assetPreview, setAssetPreview] = useState<{ count: number; loading: boolean }>({ count: 0, loading: false });
  // Phiếu phần I: version hiện hành sẽ được ghim.
  const [versionPreview, setVersionPreview] = useState<
    { loading: boolean; versionNumber?: number; templateName?: string; error?: string }>({ loading: false });

  const [userOptions, setUserOptions] = useState<UserOption[]>([]);
  const [usersLoading, setUsersLoading] = useState(false);

  const columnsState = useListColumnsState('campaign-list-columns-v2', CAMPAIGN_COLUMNS_STATE);

  const loadUsers = async () => {
    setUsersLoading(true);
    try {
      const res = await apiClient.get('/users', { params: { pageSize: 500 } });
      const users = (res.data?.data ?? []) as { id: string; firstName: string; lastName: string; username: string }[];
      setUserOptions(users.map(u => ({
        label: [u.firstName, u.lastName].filter(Boolean).join(' ') || u.username,
        value: u.id,
      })));
    } catch {
      setUserOptions([]);
    } finally {
      setUsersLoading(false);
    }
  };

  useEffect(() => {
    if (!selectedSystemId) { setAssetPreview({ count: 0, loading: false }); setVersionPreview({ loading: false }); return; }
    let alive = true;

    // Số asset gắn tại các vị trí của hệ thống (GET /systems/{id}/assets → pagination.totalItems,
    // same shape systemsService.listAssets consumers use on SystemDetailPage).
    setAssetPreview(p => ({ ...p, loading: true }));
    apiClient.get(`/systems/${selectedSystemId}/assets`, { params: { page: 1, pageSize: 1 } })
      .then(res => {
        if (!alive) return;
        const total = res.data?.pagination?.totalItems ?? res.data?.total ?? 0;
        setAssetPreview({ count: total, loading: false });
      })
      .catch(() => { if (alive) setAssetPreview({ count: 0, loading: false }); });

    // Version hiện hành của template thuộc hệ thống (ưu tiên template duy nhất; nhiều template → báo lỗi chọn tay).
    setVersionPreview({ loading: true });
    (async () => {
      try {
        const res = await apiClient.get('/maintenance/templates', { params: { systemInfoId: selectedSystemId } });
        const tpls = (res.data?.data ?? []) as Array<{ id: string; name: string; isActive: boolean; currentVersion?: { id: string; versionNumber: number } | null }>;
        if (!alive) return;
        const active = tpls.filter(t => t.isActive);
        if (active.length === 0) { setVersionPreview({ loading: false, error: 'Hệ thống chưa có mẫu bảo dưỡng.' }); return; }
        if (active.length > 1) { setVersionPreview({ loading: false, error: `${active.length} mẫu — hãy mở trình thiết kế để công bố đúng mẫu cần dùng.` }); return; }
        const cv = active[0].currentVersion;
        if (!cv) { setVersionPreview({ loading: false, error: 'Mẫu chưa có phiên bản hiện hành đã công bố.' }); return; }
        setVersionPreview({ loading: false, versionNumber: cv.versionNumber, templateName: active[0].name });
      } catch {
        if (alive) setVersionPreview({ loading: false, error: 'Lỗi tải mẫu' });
      }
    })();

    return () => { alive = false; };
  }, [selectedSystemId]);

  const openCreate = async () => {
    form.resetFields();
    setSelectedSystemId(undefined);
    setSystemsLoading(true);
    try {
      const res = await apiClient.get('/system-infos');
      setSystems((res.data?.data ?? []) as SystemOption[]);
    } catch { setSystems([]); }
    setSystemsLoading(false);
    void loadUsers();
    setCreateOpen(true);
  };

  const submitCreate = async () => {
    setSubmitting(true);
    try {
      const values = await form.validateFields();
      const payload: Record<string, unknown> = {
        systemInfoId: values.systemInfoId,
        batchNumber: values.batchNumber || null,
        startDate: values.startDate ? values.startDate.toISOString() : null,
        executorIds: values.executorIds ?? [],
      };
      if (values.reviewerId) payload.reviewerId = values.reviewerId;
      const res = await apiClient.post('/maintenance/campaigns', payload);
      message.success('Đã tạo đợt bảo dưỡng');
      setCreateOpen(false);
      actionRef.current?.reload();
      navigate(`/maintenance/campaigns/${res.data.data.id}`);
    } catch (err: unknown) {
      const e = err as { errorFields?: unknown; response?: { data?: { message?: string } } };
      if (e?.errorFields) return;
      message.error(e?.response?.data?.message || 'Lỗi tạo đợt bảo dưỡng');
    } finally {
      setSubmitting(false);
    }
  };

  const fmt = (v?: string | null) => formatDate(v);

  // ST7b — 1 fetch dùng chung cho ProTable (desktop) và ProList Card (mobile): không trùng code gọi API.
  // systemInfoId + sortBy/sortDir là thứ backend hỗ trợ (sort whitelist chạy trên DB);
  // mã đợt/trạng thái lọc ngay trên client.
  const fetchCampaigns = async (params: {
    systemInfoId?: string; sortBy?: string; sortDir?: string;
  }): Promise<CampaignDto[]> => {
    const res = await apiClient.get('/maintenance/campaigns', {
      params: {
        ...(params.systemInfoId ? { systemInfoId: params.systemInfoId } : {}),
        ...(params.sortBy && params.sortDir ? { sortBy: params.sortBy, sortDir: params.sortDir } : {}),
      },
    });
    return (res.data.data || []) as CampaignDto[];
  };

  const filterLocally = (
    data: CampaignDto[],
    filters: { status?: string; batchNumber?: string },
  ): CampaignDto[] => {
    const keyword = filters.batchNumber?.trim().toLowerCase();
    return data.filter(c =>
      (!filters.status || c.status === filters.status)
      && (!keyword || (c.batchNumber ?? '').toLowerCase().includes(keyword)));
  };

  const loadList = async (params: {
    systemInfoId?: string; status?: string; batchNumber?: string; sortBy?: string; sortDir?: string;
  }) => {
    if (!canView && !canManage) return { data: [] as CampaignDto[], success: true, total: 0 };
    try {
      const data = await fetchCampaigns(params);
      const filtered = filterLocally(data, params);
      return { data: filtered, success: true, total: filtered.length };
    } catch (err: unknown) {
      const e = err as { response?: { data?: { message?: string } } };
      void message.error(e?.response?.data?.message || 'Lỗi tải danh sách đợt bảo dưỡng');
      return { data: [] as CampaignDto[], success: false, total: 0 };
    }
  };

  const openDetail = (record: CampaignDto) => navigate(`/maintenance/campaigns/${record.id}`);

  // HOÀN THÀNH — endpoint duy nhất hiện có cho vòng đời đợt (POST /maintenance/campaigns/{id}/complete).
  const handleComplete = (record: CampaignDto) => {
    modal.confirm({
      title: 'Hoàn thành đợt bảo dưỡng?',
      content: 'Sau khi hoàn thành, kết quả kiểm tra sẽ bị khóa và không thể sửa.',
      okText: 'Hoàn thành',
      cancelText: 'Hủy',
      onOk: async () => {
        try {
          const res = await apiClient.post(`/maintenance/campaigns/${record.id}/complete`);
          const due = res.data?.data?.nextMaintenanceDueDate;
          void message.success(`Đã hoàn thành đợt bảo dưỡng${due ? ` — lần bảo dưỡng tới: ${formatDate(due)}` : ''}`);
          actionRef.current?.reload();
        } catch (err: unknown) {
          const e = err as { response?: { data?: { message?: string } } };
          void message.error(e?.response?.data?.message || 'Không thể hoàn thành đợt bảo dưỡng');
          throw new Error('complete campaign failed');
        }
      },
    });
  };

  /**
   * ONE primary action per lifecycle state — CHỈ InProgress mới có primary "Hoàn thành".
   * Mọi trạng thái khác (Completed, Closed, ...) KHÔNG có primary: backend chỉ có
   * POST /complete nên không bịa fallback "Cập nhật" nữa.
   */
  const primaryActionFor = (record: CampaignDto): ListPrimaryAction | null => {
    if (!canManage) return null;
    if (record.status === 'InProgress') {
      return {
        key: 'complete',
        label: 'Hoàn thành',
        icon: <CheckCircleOutlined />,
        onClick: () => handleComplete(record),
      };
    }
    return null;
  };

  // Menu ⋯: không bao giờ lặp lại primary. "Sửa" chỉ có khi đợt còn mở được (chưa Completed/Closed)
  // — mở trang chi tiết, nơi duy nhất nhập được kết quả.
  const menuItemsFor = (record: CampaignDto): NonNullable<MenuProps['items']> => {
    const items: NonNullable<MenuProps['items']> = [];
    if (canManage && record.status !== 'Completed' && record.status !== 'Closed') {
      items.push({ key: 'edit', label: 'Sửa', icon: <EditOutlined />, onClick: () => openDetail(record) });
    }
    return items;
  };

  const renderRowActions = (record: CampaignDto) => (
    <ListRowActions
      primary={primaryActionFor(record)}
      menuItems={menuItemsFor(record)}
      stopPropagation
    />
  );

  const searchColumns: ProColumns<CampaignDto>[] = [
    {
      title: 'Mã đợt', key: 'search-batch', dataIndex: 'batchNumber', hideInTable: true,
      fieldProps: { placeholder: 'Tìm theo mã đợt' },
    },
    {
      title: 'Hệ thống', key: 'search-system', dataIndex: 'systemInfoId', valueType: 'select', hideInTable: true,
      fieldProps: { showSearch: true, optionFilterProp: 'label', placeholder: 'Chọn hệ thống' },
      request: async () => {
        const res = await apiClient.get('/system-infos');
        return ((res.data?.data ?? []) as SystemOption[])
          .map(s => ({ label: `${s.name} (${s.code})`, value: s.id }));
      },
    },
    {
      title: 'Trạng thái', key: 'search-status', dataIndex: 'status', valueType: 'select', hideInTable: true,
      valueEnum: {
        Draft: { text: 'Nháp' },
        InProgress: { text: 'Đang thực hiện' },
        Completed: { text: 'Hoàn thành' },
        Closed: { text: 'Đã đóng' },
      },
    },
  ];

  const desktopColumns: ProColumns<CampaignDto>[] = ([
    {
      // Sort whitelist: batchnumber (collation tự nhiên trên DB).
      title: 'Mã đợt', key: 'batch', dataIndex: 'batchNumber', width: 130, sorter: true,
      // Row click mở detail; mã đợt cũng là link để bàn phím/chuột phải dùng được.
      render: (_, record) => (
        <Typography.Link style={{ whiteSpace: 'nowrap' }} onClick={() => openDetail(record)}>
          {record.batchNumber || '(không mã)'}
        </Typography.Link>
      ),
    },
    {
      title: 'Hệ thống', key: 'systemInfoName', dataIndex: 'systemInfoName', width: 160,
      render: (_, record) => <EllipsisText value={record.systemInfoName} />,
    },
    {
      title: 'Mẫu', key: 'templateName', width: 150,
      render: (_, record) => <EllipsisText value={record.templateName} />,
    },
    {
      // Sort whitelist: versionnumber (TemplateVersion.VersionNumber).
      title: 'Phiên bản', key: 'versionNumber', dataIndex: 'versionNumber', width: 80, sorter: true,
      render: (_, record) => <Tag color="blue">v{record.versionNumber}</Tag>,
    },
    {
      // Sort whitelist: status.
      title: 'Trạng thái', key: 'status', dataIndex: 'status', width: 110, sorter: true,
      render: (_, record) => {
        const s = STATUS_TAG[record.status] ?? { color: 'default', label: record.status };
        return <Tag color={s.color}>{s.label}</Tag>;
      },
    },
    {
      // Sort whitelist: startdate (mặc định desc — khớp default của BE).
      title: 'Bắt đầu', key: 'startDate', dataIndex: 'startDate', width: 100,
      sorter: true, defaultSortOrder: 'descend',
      render: (_, record) => fmt(record.startDate),
    },
    {
      // Sort whitelist: enddate.
      title: 'Kết thúc', key: 'endDate', dataIndex: 'endDate', width: 100, sorter: true,
      render: (_, record) => fmt(record.endDate),
    },
    {
      // Cột tùy chọn (mặc định ẩn) — entity-backed qua Reviewer (list DTO trả reviewerName).
      title: 'Người kiểm tra', key: 'reviewerName', width: 150,
      render: (_, record) => <EllipsisText value={record.reviewerName} />,
    },
    {
      // Cột tùy chọn (mặc định ẩn) — entity-backed qua Executors (list DTO trả executorNames).
      title: 'Người thực hiện', key: 'executorNames', width: 180,
      render: (_, record) => <EllipsisText value={record.executorNames?.filter(Boolean).join(', ')} />,
    },
    {
      title: 'Thao tác', key: 'actions', width: 150, fixed: 'right',
      render: (_, record) => renderRowActions(record),
    },
  ] as ProColumns<CampaignDto>[]).map(column => ({ ...column, hideInSearch: true, search: false }));

  // Modal tạo đợt — định nghĩa MỘT lần, render chung cho cả mobile Card và desktop Table (ST7b).
  const createModal = (
    <Modal
      open={createOpen}
      title="Tạo đợt bảo dưỡng"
      onOk={form.submit}
      onCancel={() => setCreateOpen(false)}
      confirmLoading={submitting}
      destroyOnHidden
      width={isMobile ? '95%' : 640}
      okText="Tạo"
    >
      <Form form={form} layout="vertical" onFinish={submitCreate}>
        <Form.Item
          label="Hệ thống"
          name="systemInfoId"
          rules={[{ required: true, message: 'Vui lòng chọn hệ thống' }]}
        >
          <Select
            showSearch
            loading={systemsLoading}
            placeholder="Chọn hệ thống"
            optionFilterProp="label"
            onChange={(v: string) => setSelectedSystemId(v)}
            options={systems.map(s => ({ label: `${s.name} (${s.code})`, value: s.id }))}
          />
        </Form.Item>

        {selectedSystemId && (
          <Descriptions
            size="small"
            column={1}
            bordered
            style={{ marginBottom: 16 }}
            items={[
              {
                key: 'version',
                label: (<Space size={4}><SafetyCertificateOutlined /> Danh mục kiểm tra áp dụng</Space>),
                children: versionPreview.loading
                  ? <Spin size="small" />
                  : versionPreview.error
                    ? <Alert type="warning" showIcon title={versionPreview.error} />
                    : <span>{versionPreview.templateName} — <Tag color="blue">v{versionPreview.versionNumber}</Tag> (hiện hành)</span>,
              },
              {
                key: 'assets',
                label: (<Space size={4}><UserOutlined /> Thiết bị sẽ snapshot</Space>),
                children: assetPreview.loading
                  ? <Spin size="small" />
                  : <span><b>{assetPreview.count}</b> tài sản đang lắp tại các vị trí của hệ thống</span>,
              },
            ]}
          />
        )}

        <Row gutter={12}>
          <Col span={12}>
            <Form.Item label="Ngày bắt đầu" name="startDate" initialValue={dayjs()}>
              <DatePicker style={{ width: '100%' }} />
            </Form.Item>
          </Col>
          <Col span={12}>
            <Form.Item label="Mã đợt" name="batchNumber">
              <Input placeholder="VD: DOT-2026-01" maxLength={50} />
            </Form.Item>
          </Col>
        </Row>

        <Form.Item
          label="Người thực hiện"
          name="executorIds"
          tooltip="Nhiều người cùng thực hiện đợt bảo dưỡng (cùng công ty với hệ thống)"
        >
          <Select
            mode="multiple"
            showSearch
            allowClear
            loading={usersLoading}
            placeholder="Chọn người thực hiện"
            maxTagCount="responsive"
            optionFilterProp="label"
            options={userOptions}
          />
        </Form.Item>
        <Form.Item label="Người kiểm tra" name="reviewerId">
          <Select
            showSearch
            allowClear
            loading={usersLoading}
            placeholder="Mặc định: người hoàn thành đợt"
            optionFilterProp="label"
            options={userOptions}
          />
        </Form.Item>
      </Form>
    </Modal>
  );

  // [FE-R2] Mobile: ProList Card thay Table — cùng fetch + cùng ListRowActions (ST7b pattern).
  if (isMobile) {
    return (
      <div>
        {!canManage && (
          <Alert type="info" showIcon style={{ marginBottom: 12 }}
            title="Bạn chỉ có quyền xem lịch sử đợt bảo dưỡng." />
        )}
        <ProList<CampaignDto>
          headerTitle="Danh sách đợt bảo dưỡng"
          actionRef={actionRef}
          rowKey="id"
          ghost
          cardProps={false}
          columns={searchColumns}
          // Mobile: bộ lọc mặc định THU GỌN để card chiếm màn hình.
          search={{ labelWidth: 'auto', defaultCollapsed: true }}
          grid={{ gutter: 16, xs: 1, sm: 1 }}
          toolBarRender={() => [
            canManage && (
              <Button key="add" type="primary" icon={<PlusOutlined />} onClick={() => void openCreate()}>Tạo đợt bảo dưỡng</Button>
            ),
          ]}
          request={async (params, sort) => {
            const { systemInfoId, status, batchNumber } = params as {
              systemInfoId?: string; status?: string; batchNumber?: string;
            };
            return loadList({ systemInfoId, status, batchNumber, ...resolveSortParams(sort) });
          }}
          pagination={false}
          itemRender={(record) => {
            const s = STATUS_TAG[record.status] ?? { color: 'default', label: record.status };
            return (
              <Card
                hoverable
                onClick={() => openDetail(record)}
                style={{ borderRadius: 12, marginBottom: 16, cursor: 'pointer' }}
                styles={{ body: { padding: 16 } }}
              >
                <div style={{ display: 'flex', alignItems: 'center', gap: 10, marginBottom: 8, flexWrap: 'wrap' }}>
                  <Typography.Link
                    style={{ fontSize: 15, fontWeight: 600 }}
                    onClick={(e) => { e.stopPropagation(); openDetail(record); }}
                  >
                    {record.batchNumber || '(không mã)'}
                  </Typography.Link>
                  <Tag color={s.color} style={{ marginInlineEnd: 0 }}>{s.label}</Tag>
                </div>
                <div style={{ marginBottom: 8, fontSize: 13 }}>
                  {record.systemInfoName} — <Tag color="blue" style={{ marginInlineEnd: 0 }}>v{record.versionNumber}</Tag>
                </div>
                <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: '6px 12px', padding: '10px 12px', background: '#fafafa', borderRadius: 8 }}>
                  <Text type="secondary" style={{ fontSize: 12 }}>Bắt đầu</Text>
                  <Text style={{ fontSize: 13 }}>{fmt(record.startDate)}</Text>
                  <Text type="secondary" style={{ fontSize: 12 }}>Kết thúc</Text>
                  <Text style={{ fontSize: 13 }}>{fmt(record.endDate)}</Text>
                  <Text type="secondary" style={{ fontSize: 12 }}>Kết quả</Text>
                  <Text style={{ fontSize: 13 }} title={`${record.resultsCount} dòng kết quả (mỗi tiêu chuẩn = 1 dòng)`}>
                    {record.resultsCount} dòng kết quả
                  </Text>
                </div>
                <Divider style={{ margin: '10px 0' }} />
                {renderRowActions(record)}
              </Card>
            );
          }}
        />
        {createModal}
      </div>
    );
  }

  return (
    <div>
      {!canManage && (
        <Alert type="info" showIcon style={{ marginBottom: 12 }}
          title="Bạn chỉ có quyền xem lịch sử đợt bảo dưỡng." />
      )}
      <ProTable<CampaignDto>
        headerTitle="Danh sách đợt bảo dưỡng"
        rowKey="id"
        size="small"
        columns={[...searchColumns, ...desktopColumns]}
        actionRef={actionRef}
        // Desktop: bộ lọc mở sẵn.
        search={{ labelWidth: 'auto', defaultCollapsed: false }}
        options={{ reload: true, density: true, setting: true }}
        columnsState={columnsState}
        toolBarRender={() => [
          canManage && (
            <Button type="primary" icon={<PlusOutlined />} onClick={() => void openCreate()}>Tạo đợt bảo dưỡng</Button>
          ),
        ]}
        // [FE-R4] Click-cả-row mở detail; ô hành động đã stopPropagation qua ListRowActions.
        onRow={(record) => ({
          onClick: () => openDetail(record),
          style: { cursor: 'pointer' },
        })}
        request={async (params, sort) => {
          const { systemInfoId, status, batchNumber } = params as {
            systemInfoId?: string; status?: string; batchNumber?: string;
          };
          return loadList({ systemInfoId, status, batchNumber, ...resolveSortParams(sort) });
        }}
        scroll={{ x: 'max-content' }}
      />

      {createModal}
    </div>
  );
}
