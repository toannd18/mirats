import { useEffect, useRef, useState, type ReactNode } from 'react';
import {
  Alert, App, Button, Card, Col, DatePicker, Descriptions, Divider, Form, Input, Modal, Row, Select, Space, Spin, Tag, Tooltip, Typography,
} from 'antd';
import { PlusOutlined, SafetyCertificateOutlined, UserOutlined } from '@ant-design/icons';
import { ProList, ProTable } from '@ant-design/pro-components';
import type { ActionType, ProColumns } from '@ant-design/pro-components';
import { useNavigate } from 'react-router-dom';
import dayjs, { Dayjs } from 'dayjs';
import apiClient from '../../../services/api-client';
import { usePermission } from '../../../hooks/usePermission';
import { useIsMobile } from '../../../hooks/useIsMobile';

const { Text } = Typography;

interface CampaignDto {
  id: string;
  systemInfoId: string;
  systemInfoName: string;
  templateVersionId: string;
  versionNumber: number;
  startDate?: string | null;
  endDate?: string | null;
  batchNumber?: string | null;
  companyId?: string | null;
  reviewerId?: string | null;
  status: string;
  createdAt: string;
  snapshotCount: number;
  resultsCount: number;
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
  InProgress: { color: 'processing', label: 'Đang thực hiện' },
  Completed: { color: 'success', label: 'Hoàn thành' },
};

/**
 * MC-6 — Danh sách đợt bảo dưỡng (Campaign) + tạo mới. Create chọn SystemInfo → tự hiện
 * TemplateVersion hiện hành sẽ ghim + số Asset sẽ được snapshot (Phần II phiếu gốc),
 * chọn nhiều người thực hiện + 1 người kiểm tra (Executor UI hoãn từ MC-3).
 */
export default function MaintenanceCampaignListPage() {
  const { message } = App.useApp();
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
        if (active.length === 0) { setVersionPreview({ loading: false, error: 'Hệ thống chưa có template bảo dưỡng.' }); return; }
        if (active.length > 1) { setVersionPreview({ loading: false, error: `${active.length} templates — hãy mở Builder để publish đúng template cần dùng.` }); return; }
        const cv = active[0].currentVersion;
        if (!cv) { setVersionPreview({ loading: false, error: 'Template chưa có version hiện hành đã publish.' }); return; }
        setVersionPreview({ loading: false, versionNumber: cv.versionNumber, templateName: active[0].name });
      } catch {
        if (alive) setVersionPreview({ loading: false, error: 'Lỗi tải template' });
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

  const fmt = (v?: string | null) => (v ? dayjs(v).format('DD/MM/YYYY') : '—');

  // ST7b — 1 fetch dùng chung cho ProTable (desktop) và ProList Card (mobile): không trùng code gọi API.
  const fetchCampaigns = async (): Promise<CampaignDto[]> => {
    const res = await apiClient.get('/maintenance/campaigns');
    return (res.data.data || []) as CampaignDto[];
  };

  // ST7b + T-UX1 — nút "Chi tiết" dùng chung desktop/mobile; stopPropagation để không
  // kích hoạt nhầm điều hướng click-to-detail của row/card (pattern ComponentListPage).
  const renderActions = (record: CampaignDto): ReactNode[] => [
    <Button
      key="detail"
      size="small"
      onClick={(e) => { e.stopPropagation(); navigate(`/maintenance/campaigns/${record.id}`); }}
    >
      Chi tiết
    </Button>,
  ];

  const columns: ProColumns<CampaignDto>[] = [
    {
      title: 'Mã đợt', key: 'batch', width: 150,
      // [FE-R4] Click-cả-row mở detail (onRow ở ProTable) — cột mã là text thường, không còn <a> riêng.
      render: (_, r) => <Text strong>{r.batchNumber || '(không mã)'}</Text>,
    },
    { title: 'Hệ thống', dataIndex: 'systemInfoName', key: 'systemInfoName', width: 200 },
    { title: 'Version', dataIndex: 'versionNumber', key: 'versionNumber', width: 90, render: (_, r) => <Tag color="blue">v{r.versionNumber}</Tag> },
    {
      title: 'Trạng thái', dataIndex: 'status', key: 'status', width: 130,
      render: (_, r) => {
        const s = STATUS_TAG[r.status] ?? { color: 'default', label: r.status };
        return <Tag color={s.color}>{s.label}</Tag>;
      },
    },
    { title: 'Bắt đầu', dataIndex: 'startDate', key: 'startDate', width: 110, render: (_, r) => fmt(r.startDate) },
    { title: 'Kết thúc', dataIndex: 'endDate', key: 'endDate', width: 110, render: (_, r) => fmt(r.endDate) },
    {
      // [MC-9] resultsCount = số dòng kết quả theo tiêu chuẩn (thiết bị × tiêu chuẩn), không phải số thiết bị
      title: 'Kết quả', key: 'progress', width: 130,
      render: (_, r) => (
        <Tooltip title={`${r.resultsCount} dòng kết quả (mỗi tiêu chuẩn = 1 dòng)`}>
          <span>{r.resultsCount} dòng kết quả</span>
        </Tooltip>
      ),
    },
    {
      title: 'Hành động', key: 'actions', valueType: 'option' as const, width: 120,
      render: (_, record) => (
        <Space size="small">{renderActions(record)}</Space>
      ),
    },
  ];

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
                label: (<Space size={4}><SafetyCertificateOutlined /> Checklist áp dụng</Space>),
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
            <Form.Item label="Mã đợt (Batch)" name="batchNumber">
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
        <Form.Item label="Người kiểm tra (Reviewer)" name="reviewerId">
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

  // [FE-R2] Mobile: ProList Card thay Table — cùng fetch + cùng renderActions (ST7b pattern).
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
          search={false}
          grid={{ gutter: 16, xs: 1, sm: 1 }}
          toolBarRender={() => [
            canManage && (
              <Button key="add" type="primary" icon={<PlusOutlined />} onClick={() => void openCreate()}>Tạo đợt bảo dưỡng</Button>
            ),
          ]}
          request={async () => {
            if (!canView && !canManage) return { data: [], success: true, total: 0 };
            try {
              const data = await fetchCampaigns();
              return { data, success: true, total: data.length };
            } catch (err: unknown) {
              const e = err as { response?: { data?: { message?: string } } };
              message.error(e?.response?.data?.message || 'Lỗi tải danh sách đợt bảo dưỡng');
              return { data: [], success: false, total: 0 };
            }
          }}
          pagination={false}
          itemRender={(r) => {
            const s = STATUS_TAG[r.status] ?? { color: 'default', label: r.status };
            return (
              <Card
                hoverable
                onClick={() => navigate(`/maintenance/campaigns/${r.id}`)}
                style={{ borderRadius: 12, marginBottom: 16, cursor: 'pointer' }}
                styles={{ body: { padding: 16 } }}
              >
                <div style={{ display: 'flex', alignItems: 'center', gap: 10, marginBottom: 8, flexWrap: 'wrap' }}>
                  <Text strong style={{ fontSize: 15 }}>{r.batchNumber || '(không mã)'}</Text>
                  <Tag color={s.color} style={{ marginInlineEnd: 0 }}>{s.label}</Tag>
                </div>
                <div style={{ marginBottom: 8, fontSize: 13 }}>
                  {r.systemInfoName} — <Tag color="blue" style={{ marginInlineEnd: 0 }}>v{r.versionNumber}</Tag>
                </div>
                <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: '6px 12px', padding: '10px 12px', background: '#fafafa', borderRadius: 8 }}>
                  <Text type="secondary" style={{ fontSize: 12 }}>Bắt đầu</Text>
                  <Text style={{ fontSize: 13 }}>{fmt(r.startDate)}</Text>
                  <Text type="secondary" style={{ fontSize: 12 }}>Kết thúc</Text>
                  <Text style={{ fontSize: 13 }}>{fmt(r.endDate)}</Text>
                  <Text type="secondary" style={{ fontSize: 12 }}>Kết quả</Text>
                  <Text style={{ fontSize: 13 }} title={`${r.resultsCount} dòng kết quả (mỗi tiêu chuẩn = 1 dòng)`}>
                    {r.resultsCount} dòng kết quả
                  </Text>
                </div>
                <Divider style={{ margin: '10px 0' }} />
                <Space size="small" wrap>{renderActions(r)}</Space>
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
        columns={columns}
        actionRef={actionRef}
        search={false}
        options={{ reload: true, density: true, setting: true }}
        toolBarRender={() => [
          canManage && (
            <Button type="primary" icon={<PlusOutlined />} onClick={() => void openCreate()}>Tạo đợt bảo dưỡng</Button>
          ),
        ]}
        // [FE-R4] Click-cả-row mở detail — nút Chi tiết đã stopPropagation.
        onRow={(record) => ({
          onClick: () => navigate(`/maintenance/campaigns/${record.id}`),
          style: { cursor: 'pointer' },
        })}
        request={async () => {
          if (!canView && !canManage) return { data: [], success: true, total: 0 };
          try {
            const data = await fetchCampaigns();
            return { data, success: true, total: data.length };
          } catch (err: unknown) {
            const e = err as { response?: { data?: { message?: string } } };
            message.error(e?.response?.data?.message || 'Lỗi tải danh sách đợt bảo dưỡng');
            return { data: [], success: false, total: 0 };
          }
        }}
        scroll={{ x: 'max-content' }}
      />

      {createModal}
    </div>
  );
}
