import { useEffect, useRef, useState, type ReactNode } from 'react';
import { Button, Space, Input, Tag, Form, Select, Switch, ColorPicker, Card, Divider, Typography, App } from 'antd';
import { PlusOutlined, SearchOutlined } from '@ant-design/icons';
import { ProList, ProTable } from '@ant-design/pro-components';
import type { ActionType, ProColumns } from '@ant-design/pro-components';
import apiClient from '../../../services/api-client';
import { usePermission } from '../../../hooks/usePermission';
import { useIsMobile } from '../../../hooks/useIsMobile';
import ModalFormShell from '../../../shared/components/ModalFormShell';
import ListRowActions from '../../../shared/components/list/ListRowActions';
import { applyServerFieldErrors, serverErrorMessage } from '../../../shared/utils/formErrors';

const { Text } = Typography;

// ST6b — permission gating (mirrors backend [Authorize(Policy=...)]).
const CATEGORY_TYPE_LABELS: Record<string, string> = {
  Asset: 'Tài sản',
  Consumable: 'Vật tư tiêu hao',
  Accessory: 'Phụ kiện',
  Component: 'Linh kiện',
  License: 'Bản quyền',
  1: 'Tài sản',
  2: 'Vật tư tiêu hao',
  3: 'Phụ kiện',
  4: 'Linh kiện',
  5: 'Bản quyền',
};

interface CategoryRow {
  id: string;
  name: string;
  tagColor: string;
  categoryType: string | number;
  requireAcceptance: boolean;
  checkinEmail: boolean;
  useDefaultEula: boolean;
  notes?: string;
}

export default function CategoryListPage() {
  // [FE-R6] message lấy từ App.useApp() (context theme) thay vì static import.
  const { message, modal } = App.useApp();
  const [open, setOpen] = useState(false);
  const [editingId, setEditingId] = useState<string | null>(null);
  const [form] = Form.useForm();
  // ST7b — 1 actionRef dùng chung cho ProTable (desktop) và ProList (mobile Card).
  const actionRef = useRef<ActionType | null>(null);
  const [typeFilter, setTypeFilter] = useState<string | undefined>(undefined);
  const [search, setSearch] = useState('');
  const isMobile = useIsMobile();

  useEffect(() => {
    actionRef.current?.reload();
  }, [search, typeFilter]);

  // ST6b — permission gating (mirrors backend [Authorize(Policy=...)]).
  const canCreate = usePermission('categories.create');
  const canEdit = usePermission('categories.edit');
  const canDelete = usePermission('categories.delete');

  const handleEdit = (record: CategoryRow) => {
    setEditingId(record.id);
    form.setFieldsValue({
      name: record.name,
      tagColor: record.tagColor || '#1890ff',
      categoryType: record.categoryType,
      requireAcceptance: record.requireAcceptance ?? false,
      checkinEmail: record.checkinEmail ?? false,
      useDefaultEula: record.useDefaultEula ?? true,
      notes: record.notes || '',
    });
    setOpen(true);
  };

  const handleAdd = () => {
    setEditingId(null);
    form.resetFields();
    setOpen(true);
  };

  const handleClose = () => {
    setEditingId(null);
    form.resetFields();
    setOpen(false);
  };

  // ST7b: confirm nằm ở Popconfirm trong renderActions (dùng chung 2 view) —
  // hàm này chỉ thực hiện xóa sau khi đã confirm.
  const handleDelete = async (record: CategoryRow) => {
    try {
      await apiClient.delete(`/categories/${record.id}`);
      message.success('Đã xóa danh mục');
      actionRef.current?.reload();
    } catch (err: unknown) {
      message.error(serverErrorMessage(err, 'Không thể xóa danh mục.'));
    }
  };

  const save = async () => {
    try {
      const values = await form.validateFields();
      if (editingId) {
        await apiClient.put(`/categories/${editingId}`, values);
        message.success('Đã cập nhật danh mục');
      } else {
        await apiClient.post('/categories', values);
        message.success('Đã tạo danh mục');
      }
      handleClose();
      actionRef.current?.reload();
    } catch (err: unknown) {
      if ((err as { errorFields?: unknown }).errorFields) return;
      if (!applyServerFieldErrors(form, err)) {
        message.error(serverErrorMessage(err, 'Không thể lưu danh mục. Vui lòng thử lại.'));
      }
    }
  };

  // ST7b — 1 fetch dùng chung cho ProTable (desktop) và ProList (mobile Card) — không trùng code.
  const fetchList = async () => {
    const params: Record<string, unknown> = {};
    if (typeFilter !== undefined) params.type = typeFilter;
    const r = await apiClient.get('/categories', { params });
    const needle = search.trim().toLowerCase();
    const rows = ((r.data.data ?? []) as CategoryRow[]).filter(row =>
      !needle || `${row.name} ${row.notes ?? ''}`.toLowerCase().includes(needle),
    );
    return { list: rows, total: rows.length };
  };

  // ST7b — action buttons dùng chung cho cột "Thao tác" (desktop) và Card (mobile):
  // permission-gating + handler nằm MỘT chỗ duy nhất giữa 2 view.
  const renderActions = (r: CategoryRow): ReactNode => (
    <ListRowActions
      primary={canEdit ? { key: 'edit', label: 'Sửa', onClick: () => handleEdit(r) } : null}
      menuItems={canDelete ? [{
        key: 'delete',
        label: 'Xóa danh mục',
        danger: true,
        onClick: () => modal.confirm({
          title: 'Xóa danh mục này?',
          content: `Danh mục "${r.name}" sẽ bị xóa nếu không còn dữ liệu tham chiếu.`,
          okText: 'Xóa',
          cancelText: 'Hủy',
          okButtonProps: { danger: true },
          onOk: () => handleDelete(r),
        }),
      }] : []}
      stopPropagation
    />
  );

  // Modal tạo/sửa — định nghĩa MỘT lần, render chung cho cả mobile (ProList) và desktop (ProTable).
  const formModal = (
    <ModalFormShell
      open={open}
      title={editingId ? 'Sửa danh mục' : 'Tạo danh mục'}
      onOk={() => form.submit()}
      onCancel={handleClose}
      okText={editingId ? 'Lưu thay đổi' : 'Tạo danh mục'}
      size="sm"
    >
      <Form
        form={form}
        layout="vertical"
        onFinish={save}
        initialValues={{ tagColor: '#1890ff', categoryType: 'Asset', useDefaultEula: true }}
      >
        <Form.Item label="Tên danh mục" name="name" rules={[{ required: true, message: 'Vui lòng nhập tên danh mục' }]}>
          <Input placeholder="Nhập tên danh mục" />
        </Form.Item>

        <Form.Item
          label="Màu"
          name="tagColor"
          getValueFromEvent={(color) => (typeof color === 'string' ? color : color.toHexString())}
        >
          <ColorPicker format="hex" showText />
        </Form.Item>

        <Form.Item label="Loại" name="categoryType" rules={[{ required: true, message: 'Vui lòng chọn loại danh mục' }]}>
          <Select disabled={!!editingId}>
            {Object.entries(CATEGORY_TYPE_LABELS).filter(([k]) => !/^\d+$/.test(k)).map(([key, label]) => (
              <Select.Option key={key} value={key}>{label}</Select.Option>
            ))}
          </Select>
        </Form.Item>

        <Form.Item label="Bắt buộc xác nhận" name="requireAcceptance" valuePropName="checked">
          <Switch />
        </Form.Item>

        <Form.Item label="Gửi email khi thu hồi" name="checkinEmail" valuePropName="checked">
          <Switch />
        </Form.Item>

        <Form.Item label="EULA Mặc định" name="useDefaultEula" valuePropName="checked">
          <Switch />
        </Form.Item>

        <Form.Item label="Ghi chú" name="notes">
          <Input.TextArea rows={2} placeholder="Ghi chú thêm về danh mục" />
        </Form.Item>
      </Form>
    </ModalFormShell>
  );

  const columns: ProColumns<CategoryRow>[] = [
    { title: 'Tên', dataIndex: 'name', key: 'name' },
    {
      title: 'Màu',
      dataIndex: 'tagColor',
      key: 'tagColor',
      render: (_, r) => r.tagColor ? <Tag color={r.tagColor}>{r.tagColor}</Tag> : '-',
    },
    {
      title: 'Loại',
      dataIndex: 'categoryType',
      key: 'categoryType',
      render: (_, r) => <Tag>{CATEGORY_TYPE_LABELS[r.categoryType] || `#${r.categoryType}`}</Tag>,
    },
    {
      title: 'Chính sách',
      key: 'policies',
      render: (_, r) => (
        <Space size={4} wrap>
          {r.requireAcceptance && <Tag color="orange">Cần xác nhận</Tag>}
          {r.checkinEmail && <Tag color="blue">Gửi Email</Tag>}
          {r.useDefaultEula && <Tag color="green">EULA Mặc định</Tag>}
        </Space>
      ),
    },
    {
      title: 'Thao tác',
      key: 'actions',
      valueType: 'option' as const,
      width: 120,
      render: (_, r) => renderActions(r),
    },
  ];

  const mobileFilterBar = (
    <Card size="small" style={{ marginBottom: 12 }}>
      <div style={{ display: 'grid', gridTemplateColumns: '1fr', gap: 8 }}>
        <Input
          allowClear
          prefix={<SearchOutlined />}
          placeholder="Tìm danh mục…"
          value={search}
          onChange={e => setSearch(e.target.value)}
        />
        <Select
          allowClear
          placeholder="Lọc theo loại"
          style={{ width: '100%' }}
          value={typeFilter}
          onChange={v => setTypeFilter(v)}
          options={Object.entries(CATEGORY_TYPE_LABELS).filter(([k]) => !/^\d+$/.test(k)).map(([key, label]) => ({ label, value: key }))}
        />
        {canCreate && (
          <Button type="primary" icon={<PlusOutlined />} onClick={handleAdd}>
            Thêm danh mục
          </Button>
        )}
      </div>
    </Card>
  );

  // ─── Mobile (ST7b): ProList Card thay Table — cùng fetch + cùng renderActions ───
  if (isMobile) {
    return (
      <div>
        {mobileFilterBar}
        <ProList<CategoryRow>
          headerTitle="Danh sách danh mục"
          actionRef={actionRef}
          rowKey="id"
          ghost
          cardProps={false}
          search={false}
          grid={{ gutter: 16, xs: 1, sm: 1 }}
          request={async () => {
            try {
              const { list, total } = await fetchList();
              return { data: list, success: true, total };
            } catch {
              message.error('Lỗi tải danh mục');
              return { data: [], success: false, total: 0 };
            }
          }}
          pagination={{ defaultPageSize: 20, showSizeChanger: false }}
          locale={{ emptyText: 'Chưa có danh mục. Bấm Thêm danh mục để tạo mới.' }}
          itemRender={(record) => (
            <Card hoverable style={{ borderRadius: 12, marginBottom: 16 }} styles={{ body: { padding: 16 } }}>
              <div style={{ display: 'flex', alignItems: 'center', gap: 10, marginBottom: 10 }}>
                {record.tagColor && (
                  <span aria-hidden style={{ width: 12, height: 12, borderRadius: '50%', background: record.tagColor, flexShrink: 0 }} />
                )}
                <Text strong style={{ fontSize: 15 }}>{record.name}</Text>
                <Tag style={{ marginInlineEnd: 0 }} color="blue">
                  {CATEGORY_TYPE_LABELS[record.categoryType] || `#${record.categoryType}`}
                </Tag>
              </div>
              <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: '6px 12px', padding: '10px 12px', background: '#fafafa', borderRadius: 8 }}>
                <Text type="secondary" style={{ fontSize: 12 }}>Màu</Text>
                <Text style={{ fontSize: 13 }}>{record.tagColor ? <Tag color={record.tagColor} style={{ margin: 0 }}>{record.tagColor}</Tag> : '-'}</Text>
                <Text type="secondary" style={{ fontSize: 12 }}>Chính sách</Text>
                <Text style={{ fontSize: 13 }}>
                  {(record.requireAcceptance || record.checkinEmail || record.useDefaultEula)
                    ? (
                      <Space size={4} wrap>
                        {record.requireAcceptance && <Tag color="orange" style={{ margin: 0 }}>Cần xác nhận</Tag>}
                        {record.checkinEmail && <Tag color="blue" style={{ margin: 0 }}>Gửi Email</Tag>}
                        {record.useDefaultEula && <Tag color="green" style={{ margin: 0 }}>EULA</Tag>}
                      </Space>
                    )
                    : '-'}
                </Text>
              </div>
              <Divider style={{ margin: '10px 0' }} />
              {renderActions(record)}
            </Card>
          )}
        />
        {formModal}
      </div>
    );
  }

  return (
    <div>
      <Space style={{ marginBottom: 16 }} wrap>
        <Input
          allowClear
          prefix={<SearchOutlined />}
          placeholder="Tìm danh mục…"
          value={search}
          onChange={e => { setSearch(e.target.value); actionRef.current?.reload(); }}
          style={{ width: 220 }}
        />
        <Select
          allowClear
          placeholder="Lọc theo loại"
          style={{ minWidth: 180 }}
          value={typeFilter}
          onChange={v => { setTypeFilter(v); actionRef.current?.reload(); }}
          options={Object.entries(CATEGORY_TYPE_LABELS).filter(([k]) => !/^\d+$/.test(k)).map(([key, label]) => ({ label, value: key }))}
        />
        {canCreate && (
          <Button type="primary" icon={<PlusOutlined />} onClick={handleAdd}>
            Thêm danh mục
          </Button>
        )}
      </Space>

      <ProTable<CategoryRow>
        headerTitle="Danh sách danh mục"
        rowKey="id"
        size="small"
        columns={columns}
        actionRef={actionRef}
        search={false}
        options={{ reload: true, density: true, setting: true }}
        request={async () => {
          try {
            const { list, total } = await fetchList();
            return { data: list, success: true, total };
          } catch {
            message.error('Lỗi tải danh mục');
            return { data: [], success: false, total: 0 };
          }
        }}
        pagination={{ defaultPageSize: 20, showSizeChanger: true }}
        locale={{ emptyText: 'Chưa có danh mục. Bấm Thêm danh mục để tạo mới.' }}
        scroll={{ x: 'max-content' }}
      />

      {formModal}
    </div>
  );
}
