import { useEffect, useRef, useState } from 'react';
import { App, Button, Card, Divider, Form, Input, InputNumber, Typography } from 'antd';
import { PlusOutlined, ReloadOutlined, SearchOutlined } from '@ant-design/icons';
import { ProList, ProTable } from '@ant-design/pro-components';
import type { ActionType, ProColumns } from '@ant-design/pro-components';
import apiClient from '../../../services/api-client';
import { usePermission } from '../../../hooks/usePermission';
import { useIsMobile } from '../../../hooks/useIsMobile';
import ModalFormShell from '../../../shared/components/ModalFormShell';
import { serverErrorMessage } from '../../../shared/utils/formErrors';

const { Text } = Typography;

/**
 * DepreciationListPage — trang quản trị cấu hình khấu hao (master-data tham chiếu toàn cục,
 * không company-scoped: bảng depreciations không có cột CompanyId).
 *
 * T-CLEAN1 (2026-08-22): bản cũ là 1 dòng minified 834 byte từ Initial commit (scaffold chưa
 * hoàn thiện — Table thô, không ProTable/toolbar/permission gate). Refactor về đúng chuẩn các
 * trang admin chị em (CompanyListPage pattern): ProTable + request + toolBarRender.
 *
 * Trang CHỈ XEM: backend GET /depreciations hiện là endpoint duy nhất của resource này
 * (read-only, không CRUD — workflow doc §210), nên không có nút create/edit/delete;
 * `depreciations.view` dùng để gate menu/route (App.tsx permMap) và policy backend.
 */
export default function DepreciationListPage() {
  // [FE-R6] message lấy từ App.useApp() (context theme) thay vì static import.
  const { message } = App.useApp();
  const actionRef = useRef<ActionType | null>(null);
  const isMobile = useIsMobile();
  const [search, setSearch] = useState('');
  const [open, setOpen] = useState(false);
  const [saving, setSaving] = useState(false);
  const [form] = Form.useForm<{ name: string; months: number }>();

  // Gate toolbar đồng bộ policy backend `depreciations.view` (superuser pass qua PermissionHandler).
  const canView = usePermission('depreciations.view');
  const canCreate = usePermission('depreciations.create');

  interface Depreciation {
    id: string;
    name: string;
    months: number;
  }

  // ST7b — 1 fetch dùng chung cho ProTable (desktop) và ProList (mobile Card).
  const fetchList = async () => {
    const r = await apiClient.get('/depreciations');
    const rows = ((r.data.data || []) as Depreciation[]).filter(row =>
      row.name.toLowerCase().includes(search.trim().toLowerCase()),
    );
    return { list: rows, total: rows.length };
  };

  const save = async () => {
    try {
      const values = await form.validateFields();
      setSaving(true);
      await apiClient.post('/depreciations', values);
      message.success('Đã tạo cấu hình khấu hao');
      setOpen(false);
      form.resetFields();
      actionRef.current?.reload();
    } catch (err) {
      message.error(serverErrorMessage(err, 'Không thể tạo cấu hình khấu hao'));
    } finally {
      setSaving(false);
    }
  };

  const columns: ProColumns<Depreciation>[] = [
    { title: 'Tên', dataIndex: 'name', key: 'name' },
    { title: 'Số tháng', dataIndex: 'months', key: 'months', width: 120 },
  ];

  useEffect(() => {
    actionRef.current?.reload();
  }, [search]);

  const filterBar = (
    <Card size="small" style={{ marginBottom: 12 }}>
      <Input
        allowClear
        prefix={<SearchOutlined />}
        placeholder="Tìm cấu hình khấu hao…"
        value={search}
        onChange={e => setSearch(e.target.value)}
      />
    </Card>
  );

  const createButton = canCreate && (
    <Button type="primary" icon={<PlusOutlined />} onClick={() => { form.resetFields(); setOpen(true); }}>
      Tạo cấu hình
    </Button>
  );

  const createModal = (
    <ModalFormShell
      open={open}
      title="Tạo cấu hình khấu hao"
      onOk={() => form.submit()}
      onCancel={() => setOpen(false)}
      okText="Tạo cấu hình"
      confirmLoading={saving}
      size="sm"
    >
      <Form form={form} layout="vertical" onFinish={save}>
        <Form.Item label="Tên cấu hình" name="name" rules={[{ required: true, message: 'Vui lòng nhập tên cấu hình' }]}>
          <Input maxLength={255} placeholder="VD: Khấu hao 36 tháng" />
        </Form.Item>
        <Form.Item label="Số tháng" name="months" rules={[{ required: true, message: 'Vui lòng nhập số tháng' }]}>
          <InputNumber min={1} max={600} style={{ width: '100%' }} placeholder="VD: 36" />
        </Form.Item>
      </Form>
    </ModalFormShell>
  );

  // ─── Mobile (ST7b): ProList Card thay Table — cùng fetch ───
  if (isMobile) {
    return (
      <div>
        {filterBar}
        <ProList<Depreciation>
          headerTitle="Cấu hình khấu hao"
          actionRef={actionRef}
          rowKey="id"
          ghost
          cardProps={false}
          search={false}
          params={{ search }}
          grid={{ gutter: 16, xs: 1, sm: 1 }}
          toolBarRender={() => [
            canCreate && <span key="create">{createButton}</span>,
            canView && (
              <Button key="reload" icon={<ReloadOutlined />} onClick={() => actionRef.current?.reload()}>
                Tải lại
              </Button>
            ),
          ]}
          request={async () => {
            try {
              const { list, total } = await fetchList();
              return { data: list, success: true, total };
            } catch {
              message.error('Lỗi tải cấu hình khấu hao');
              return { data: [], success: false, total: 0 };
            }
          }}
          locale={{ emptyText: 'Chưa có cấu hình khấu hao. Tạo cấu hình để dùng cho báo cáo và Model.' }}
          pagination={{ defaultPageSize: 20, showSizeChanger: false }}
          itemRender={(record) => (
            <Card hoverable style={{ borderRadius: 12, marginBottom: 16 }} styles={{ body: { padding: 16 } }}>
              <div style={{ display: 'flex', alignItems: 'center', gap: 10, marginBottom: 10 }}>
                <Text strong style={{ fontSize: 15 }}>{record.name}</Text>
              </div>
              <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: '6px 12px', padding: '10px 12px', background: '#fafafa', borderRadius: 8 }}>
                <Text type="secondary" style={{ fontSize: 12 }}>Số tháng</Text>
                <Text style={{ fontSize: 13 }}>{record.months}</Text>
              </div>
              <Divider style={{ margin: '10px 0' }} />
              <Text type="secondary" style={{ fontSize: 12 }}>Cấu hình khấu hao dùng chung cho báo cáo và Model.</Text>
            </Card>
          )}
        />
        {createModal}
      </div>
    );
  }

  return (
    <div>
      {filterBar}
      <ProTable<Depreciation>
        headerTitle="Cấu hình khấu hao"
        rowKey="id"
        size="small"
        columns={columns}
        actionRef={actionRef}
        search={false}
        params={{ search }}
        options={{ reload: true, density: true, setting: true }}
        toolBarRender={() => [
          canCreate && <span key="create">{createButton}</span>,
          canView && (
            <Button key="reload" icon={<ReloadOutlined />} onClick={() => actionRef.current?.reload()}>
              Tải lại
            </Button>
          ),
        ]}
        request={async () => {
          try {
            const { list, total } = await fetchList();
            return { data: list, success: true, total };
          } catch {
            message.error('Lỗi tải cấu hình khấu hao');
            return { data: [], success: false, total: 0 };
          }
        }}
        locale={{ emptyText: 'Chưa có cấu hình khấu hao. Tạo cấu hình để dùng cho báo cáo và Model.' }}
        pagination={false}
        scroll={{ x: 'max-content' }}
      />
      {createModal}
    </div>
  );
}
