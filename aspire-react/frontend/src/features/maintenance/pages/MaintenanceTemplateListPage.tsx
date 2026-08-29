import { useRef, useState, type ReactNode } from 'react';
import {
  App, Button, Card, Divider, Form, Input, Modal, Popconfirm, Select, Space, Switch, Tag, Tooltip, Typography,
} from 'antd';
import { PlusOutlined, EditOutlined, DeleteOutlined, ExperimentOutlined, ClusterOutlined } from '@ant-design/icons';
import { ProList, ProTable } from '@ant-design/pro-components';
import type { ActionType, ProColumns } from '@ant-design/pro-components';
import { useNavigate } from 'react-router-dom';
import apiClient from '../../../services/api-client';
import { usePermission } from '../../../hooks/usePermission';
import { useIsMobile } from '../../../hooks/useIsMobile';
import CompanyTreeSelect from '../../../components/common/CompanyTreeSelect';
import type { CompanyNode } from '../../../components/common/CompanyTreeSelect';
// [FE-R3] Màu UI mới đi qua token dùng chung (T-TOKEN1) — không hard-code hex trong page.
import { textColors, uiColors } from '../../../theme/designTokens';

const { Text } = Typography;

interface SystemOption {
  id: string;
  code: string;
  name: string;
  companyId?: string | null;
}

interface TemplateDto {
  id: string;
  name: string;
  isActive: boolean;
  companyId?: string | null;
  company?: { id: string; name: string } | null;
  systemInfo: { id: string; code: string; name: string };
  versionsCount: number;
  campaignCount: number;
  currentVersion?: { id: string; versionNumber: number; publishedAt?: string | null; itemsCount: number; paramsCount: number } | null;
}

interface TemplateFormValues {
  name?: string;
  systemInfoId?: string;
  companyId?: string;
  isActive?: boolean;
}

/**
 * MC-5 — Danh sách Template bảo dưỡng (checklist). CRUD company-scoped qua API MC-2
 * (maintenance.templates). "Quản lý phiên bản" mở TemplateBuilderPage theo từng template.
 */
export default function MaintenanceTemplateListPage() {
  const { message } = App.useApp();
  const navigate = useNavigate();
  // [FE-R2/FE-R4] Branch desktop Table ↔ mobile Card theo hook dùng chung (T-RESP1).
  const isMobile = useIsMobile();
  const [open, setOpen] = useState(false);
  const [editingId, setEditingId] = useState<string | null>(null);
  const [form] = Form.useForm<TemplateFormValues>();
  const actionRef = useRef<ActionType | null>(null);

  const canManage = usePermission('maintenance.templates');

  const [systems, setSystems] = useState<SystemOption[]>([]);
  const [systemsLoading, setSystemsLoading] = useState(false);

  // Systems picker — scoped list (same /system-infos endpoint the admin page uses).
  const loadSystems = async () => {
    setSystemsLoading(true);
    try {
      const res = await apiClient.get('/system-infos');
      setSystems((res.data?.data ?? []) as SystemOption[]);
    } catch {
      setSystems([]);
    } finally {
      setSystemsLoading(false);
    }
  };

  const handleAdd = () => {
    setEditingId(null);
    form.resetFields();
    void loadSystems();
    setOpen(true);
  };

  const handleEdit = (r: TemplateDto) => {
    setEditingId(r.id);
    form.setFieldsValue({
      name: r.name,
      systemInfoId: r.systemInfo.id,
      companyId: r.companyId ?? undefined,
      isActive: r.isActive,
    });
    void loadSystems();
    setOpen(true);
  };

  const handleClose = () => {
    setEditingId(null);
    form.resetFields();
    setOpen(false);
  };

  const save = async () => {
    try {
      const values = await form.validateFields();
      if (editingId) {
        await apiClient.put(`/maintenance/templates/${editingId}`, values);
        message.success('Đã cập nhật template');
      } else {
        await apiClient.post('/maintenance/templates', values);
        message.success('Đã tạo template');
      }
      handleClose();
      actionRef.current?.reload();
    } catch (err: unknown) {
      const e = err as { errorFields?: unknown; response?: { data?: { message?: string; error_code?: string } } };
      if (e?.errorFields) return;
      message.error(e?.response?.data?.message || 'Lỗi lưu template');
    }
  };

  const handleDelete = async (record: TemplateDto) => {
    try {
      await apiClient.delete(`/maintenance/templates/${record.id}`);
      message.success('Đã xóa template');
      actionRef.current?.reload();
    } catch (err: unknown) {
      const e = err as { response?: { data?: { message?: string } } };
      message.error(e?.response?.data?.message || 'Không thể xóa template');
    }
  };

  // ST7b — 1 fetch dùng chung cho ProTable (desktop) và ProList Card (mobile): không trùng code gọi API.
  const fetchTemplates = async (): Promise<TemplateDto[]> => {
    const res = await apiClient.get('/maintenance/templates');
    return (res.data.data || []) as TemplateDto[];
  };

  // ST7b + T-UX1 — action buttons dùng chung desktop/mobile; MỌI nút phải stopPropagation
  // để không kích hoạt nhầm điều hướng click-to-detail của row/card (pattern ComponentListPage).
  const renderActions = (record: TemplateDto): ReactNode[] => [
    canManage && (
      <Tooltip key="manage" title="Quản lý phiên bản">
        <Button size="small" icon={<ExperimentOutlined />} onClick={(e) => { e.stopPropagation(); navigate(`/maintenance/templates/${record.id}`); }} />
      </Tooltip>
    ),
    canManage && (
      <Tooltip key="edit" title="Sửa">
        <Button size="small" icon={<EditOutlined />} onClick={(e) => { e.stopPropagation(); handleEdit(record); }} />
      </Tooltip>
    ),
    canManage && (
      <Popconfirm key="del" title="Xóa template?" onConfirm={() => handleDelete(record)}>
        <Tooltip title="Xóa">
          <Button size="small" danger icon={<DeleteOutlined />} onClick={(e) => e.stopPropagation()} />
        </Tooltip>
      </Popconfirm>
    ),
  ].filter(Boolean) as ReactNode[];

  const columns: ProColumns<TemplateDto>[] = [
    {
      title: 'Tên template', dataIndex: 'name', key: 'name', width: 220,
      // [FE-R4] Click-cả-row mở detail (onRow ở ProTable) — cột tên là text thường, không còn <a> riêng.
      render: (_, r) => <Text strong>{r.name}</Text>,
    },
    {
      title: 'Hệ thống', key: 'systemInfo', width: 220,
      render: (_, r) => (
        <Space size={4}>
          <ClusterOutlined style={{ fontSize: 12, color: uiColors.labelGray }} />
          <span>{r.systemInfo.name} <span style={{ color: textColors.secondary, fontSize: 12 }}>({r.systemInfo.code})</span></span>
        </Space>
      ),
    },
    {
      title: 'Công ty', key: 'company', width: 150,
      render: (_, r) => r.company?.name || <Tag>Floater</Tag>,
    },
    {
      title: 'Phiên bản', key: 'versions', width: 200,
      render: (_, r) => {
        if (!r.currentVersion) return <Tag>Chưa publish</Tag>;
        const v = r.currentVersion;
        return (
          <Space size={6}>
            <Tag color="blue">v{v.versionNumber}</Tag>
            <span style={{ fontSize: 12, color: textColors.secondary }}>
              {v.itemsCount} mục · {v.paramsCount} tiêu chuẩn
            </span>
          </Space>
        );
      },
    },
    {
      title: 'Đợt bảo dưỡng', dataIndex: 'campaignCount', key: 'campaignCount', width: 120,
      render: (_, r) => <Tag color={r.campaignCount > 0 ? 'green' : 'default'}>{r.campaignCount}</Tag>,
    },
    {
      title: 'Kích hoạt', dataIndex: 'isActive', key: 'isActive', width: 100,
      render: (_, r) => (r.isActive ? <Tag color="success">Có</Tag> : <Tag>Không</Tag>),
    },
    {
      title: 'Hành động', key: 'actions', valueType: 'option' as const, width: 190,
      render: (_, record) => (
        <Space size="small">{renderActions(record)}</Space>
      ),
    },
  ];

  // Modal tạo/sửa — định nghĩa MỘT lần, render chung cho cả mobile Card và desktop Table (ST7b).
  const formModal = (
    <Modal
      open={open}
      title={editingId ? 'Sửa template' : 'Tạo template bảo dưỡng'}
      onOk={form.submit}
      onCancel={handleClose}
      destroyOnHidden
      width={isMobile ? '95%' : 520}
    >
      <Form form={form} layout="vertical" onFinish={save}>
        <Form.Item
          label="Tên template"
          name="name"
          rules={[{ required: true, message: 'Vui lòng nhập tên template' }]}
        >
          <Input placeholder="VD: Checklist bảo dưỡng định kỳ" maxLength={255} />
        </Form.Item>
        <Form.Item
          label="Hệ thống áp dụng"
          name="systemInfoId"
          rules={[{ required: true, message: 'Vui lòng chọn hệ thống' }]}
          tooltip="Template quản lý checklist bảo dưỡng cho MỘT hệ thống"
        >
          <Select
            showSearch
            loading={systemsLoading}
            placeholder="Chọn hệ thống"
            optionFilterProp="label"
            options={systems.map(s => ({
              label: `${s.name} (${s.code})`,
              value: s.id,
            }))}
          />
        </Form.Item>
        <Form.Item
          label="Công ty"
          name="companyId"
          tooltip="Bỏ trống = floater (dùng chung mọi công ty)."
        >
          <CompanyTreeSelect
            placeholder="Chọn công ty (bỏ trống = floater)"
            allowQuickAdd={false}
          />
        </Form.Item>
        {editingId && (
          <Form.Item label="Kích hoạt" name="isActive" valuePropName="checked">
            <Switch />
          </Form.Item>
        )}
      </Form>
    </Modal>
  );

  // [FE-R2] Mobile: ProList Card thay Table — cùng fetch + cùng renderActions (ST7b pattern).
  if (isMobile) {
    return (
      <div>
        <ProList<TemplateDto>
          headerTitle="Danh sách template bảo dưỡng"
          actionRef={actionRef}
          rowKey="id"
          ghost
          cardProps={false}
          search={false}
          grid={{ gutter: 16, xs: 1, sm: 1 }}
          toolBarRender={() => [
            canManage && (
              <Button key="add" type="primary" icon={<PlusOutlined />} onClick={handleAdd}>Tạo template</Button>
            ),
          ]}
          request={async () => {
            try {
              const data = await fetchTemplates();
              return { data, success: true, total: data.length };
            } catch (err: unknown) {
              const e = err as { response?: { data?: { message?: string } } };
              message.error(e?.response?.data?.message || 'Lỗi tải danh sách template');
              return { data: [], success: false, total: 0 };
            }
          }}
          pagination={false}
          itemRender={(r) => (
            <Card
              hoverable
              onClick={() => navigate(`/maintenance/templates/${r.id}`)}
              style={{ borderRadius: 12, marginBottom: 16, cursor: 'pointer' }}
              styles={{ body: { padding: 16 } }}
            >
              <div style={{ display: 'flex', alignItems: 'center', gap: 10, marginBottom: 8, flexWrap: 'wrap' }}>
                <Text strong style={{ fontSize: 15 }}>{r.name}</Text>
                {r.isActive ? <Tag color="success" style={{ marginInlineEnd: 0 }}>Đang kích hoạt</Tag> : <Tag style={{ marginInlineEnd: 0 }}>Ngừng kích hoạt</Tag>}
              </div>
              <div style={{ marginBottom: 8 }}>
                <Space size={4} wrap>
                  <ClusterOutlined style={{ fontSize: 12, color: uiColors.labelGray }} />
                  <span style={{ fontSize: 13 }}>{r.systemInfo.name}</span>
                  <span style={{ fontSize: 12, color: textColors.secondary }}>({r.systemInfo.code})</span>
                </Space>
              </div>
              <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: '6px 12px', padding: '10px 12px', background: '#fafafa', borderRadius: 8 }}>
                <Text type="secondary" style={{ fontSize: 12 }}>Công ty</Text>
                <Text style={{ fontSize: 13 }}>{r.company?.name || 'Floater'}</Text>
                <Text type="secondary" style={{ fontSize: 12 }}>Phiên bản</Text>
                <Text style={{ fontSize: 13 }}>
                  {r.currentVersion
                    ? <>v{r.currentVersion.versionNumber} — {r.currentVersion.itemsCount} mục · {r.currentVersion.paramsCount} tiêu chuẩn</>
                    : <Tag style={{ marginInlineEnd: 0 }}>Chưa publish</Tag>}
                </Text>
                <Text type="secondary" style={{ fontSize: 12 }}>Đợt bảo dưỡng</Text>
                <Text style={{ fontSize: 13 }}>{r.campaignCount}</Text>
              </div>
              <Divider style={{ margin: '10px 0' }} />
              <Space size="small" wrap>{renderActions(r)}</Space>
            </Card>
          )}
        />
        {formModal}
      </div>
    );
  }

  return (
    <div>
      <ProTable<TemplateDto>
        headerTitle="Danh sách template bảo dưỡng"
        rowKey="id"
        size="small"
        columns={columns}
        actionRef={actionRef}
        search={false}
        options={{ reload: true, density: true, setting: true }}
        toolBarRender={() => [
          canManage && (
            <Button type="primary" icon={<PlusOutlined />} onClick={handleAdd}>Tạo template</Button>
          ),
        ]}
        // [FE-R4] Click-cả-row mở detail — nút trong cột Hành động đã stopPropagation.
        onRow={(record) => ({
          onClick: () => navigate(`/maintenance/templates/${record.id}`),
          style: { cursor: 'pointer' },
        })}
        request={async () => {
          try {
            const data = await fetchTemplates();
            return { data, success: true, total: data.length };
          } catch (err: unknown) {
            const e = err as { response?: { data?: { message?: string } } };
            message.error(e?.response?.data?.message || 'Lỗi tải danh sách template');
            return { data: [], success: false, total: 0 };
          }
        }}
        scroll={{ x: 'max-content' }}
      />

      {formModal}
    </div>
  );
}

// Type-only re-export để call-site khác (nếu cần) dùng CompanyNode
export type { CompanyNode };
