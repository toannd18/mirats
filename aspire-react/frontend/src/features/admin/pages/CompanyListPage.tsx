import { useState, useRef, type ReactNode } from 'react';
import { Button, Space, Modal, Form, Input, TreeSelect, Card, Divider, Tag, Tooltip, Typography, Popconfirm, App } from 'antd';
import { PlusOutlined, EditOutlined, DeleteOutlined } from '@ant-design/icons';
import { ProList, ProTable } from '@ant-design/pro-components';
import type { ActionType, ProColumns } from '@ant-design/pro-components';
import apiClient from '../../../services/api-client';
import { usePermission } from '../../../hooks/usePermission';
import { useIsMobile } from '../../../hooks/useIsMobile';

const { Text } = Typography;

interface CompanyNode {
  id: string;
  name: string;
  code: string;
  parentId: string | null;
  children: CompanyNode[];
}

interface TreeNode {
  title: string;
  value: string;
  disabled?: boolean;
  children?: TreeNode[];
}

/** Dẹt hóa cây công ty thành danh sách card mobile (kèm cờ isChild để hiển thị nhãn). */
function flattenCompanies(nodes: CompanyNode[], isChild = false): { node: CompanyNode; isChild: boolean }[] {
  return nodes.flatMap(n => [{ node: n, isChild }, ...flattenCompanies(n.children ?? [], true)]);
}

export default function CompanyListPage() {
  // [FE-R6] message lấy từ App.useApp() (context theme) thay vì static import.
  const { message } = App.useApp();
  const [tree, setTree] = useState<CompanyNode[]>([]);
  const [open, setOpen] = useState(false);
  const [editingId, setEditingId] = useState<string | null>(null);
  const [selectedParentId, setSelectedParentId] = useState<string | null>(null);
  const [form] = Form.useForm();
  // ST7b — 1 actionRef dùng chung cho ProTable (desktop) và ProList (mobile Card).
  const actionRef = useRef<ActionType | null>(null);
  const isMobile = useIsMobile();

  // ST6b — permission gating (mirrors backend [Authorize(Policy=...)]).
  const canCreate = usePermission('companies.create');
  const canEdit = usePermission('companies.edit');
  const canDelete = usePermission('companies.delete');

  /**
   * Convert tree data to TreeSelect format.
   * BUSINESS RULE: Only 2 levels allowed (Root → Child).
   * Child companies (level 2) are disabled in the TreeSelect
   * so users cannot select them as a parent.
   */
  const toTreeSelect = (nodes: CompanyNode[], isRoot = true): TreeNode[] =>
    nodes.map(n => ({
      title: `${n.name} (${n.code || '-'})`,
      value: n.id,
      // Disable child companies — they cannot be selected as parent
      disabled: !isRoot,
      children: n.children?.length ? toTreeSelect(n.children, false) : undefined,
    }));

  const handleEdit = (record: CompanyNode) => {
    setEditingId(record.id);
    setSelectedParentId(null);
    form.setFieldsValue({ name: record.name, code: record.code, parentId: record.parentId });
    setOpen(true);
  };

  const handleAdd = (parentId?: string) => {
    setEditingId(null);
    setSelectedParentId(parentId || null);
    form.resetFields();
    if (parentId) {
      form.setFieldValue('parentId', parentId);
    }
    setOpen(true);
  };

  const handleClose = () => {
    setEditingId(null);
    setSelectedParentId(null);
    form.resetFields();
    setOpen(false);
  };

  // ST7b: confirm nằm ở Popconfirm trong renderActions (dùng chung 2 view) —
  // hàm này chỉ thực hiện xóa sau khi đã confirm.
  const handleDelete = async (record: CompanyNode) => {
    try {
      await apiClient.delete(`/companies/${record.id}`);
      message.success('Đã xóa');
      actionRef.current?.reload();
    } catch (err: unknown) {
      const e = err as { response?: { data?: { message?: string } } };
      message.error(e?.response?.data?.message || 'Không thể xóa');
    }
  };

  const save = async () => {
    try {
      const values = await form.validateFields();

      // BUSINESS RULE: Double-check that parentId is not a child company (level 2)
      if (values.parentId) {
        const findNode = (nodes: CompanyNode[], id: string): CompanyNode | null => {
          for (const n of nodes) {
            if (n.id === id) return n;
            if (n.children?.length) {
              const found = findNode(n.children, id);
              if (found) return found;
            }
          }
          return null;
        };
        const selectedParent = findNode(tree, values.parentId);
        // If the selected parent itself has a parentId, it's a child company — reject
        if (selectedParent && selectedParent.parentId) {
          message.warning('Không thể chọn công ty con làm công ty cha. Hệ thống chỉ hỗ trợ tối đa 2 cấp.');
          return;
        }
      }

      if (editingId) {
        await apiClient.put(`/companies/${editingId}`, values);
        message.success('Đã cập nhật');
      } else {
        await apiClient.post('/companies', values);
        message.success('Đã tạo công ty');
      }
      handleClose();
      actionRef.current?.reload();
    } catch (err: unknown) {
      if ((err as { errorFields?: unknown }).errorFields) return;
      const e = err as { response?: { data?: { message?: string } } };
      message.error(e?.response?.data?.message || 'Lỗi lưu');
    }
  };

  // ST7b — 1 fetch dùng chung cho ProTable (desktop) và ProList (mobile Card).
  const fetchTree = async (): Promise<CompanyNode[]> => {
    const r = await apiClient.get('/companies');
    return (r.data.data || []) as CompanyNode[];
  };

  // ST7b — action buttons dùng chung desktop/mobile.
  // BUSINESS RULE: Only Root companies (parentId == null) can have child companies
  const renderActions = (record: CompanyNode): ReactNode[] => [
    canCreate && record.parentId == null && (
      <Tooltip key="addChild" title="Thêm">
        <Button size="small" icon={<PlusOutlined />} onClick={() => handleAdd(record.id)} />
      </Tooltip>
    ),
    canEdit && (
      <Tooltip key="edit" title="Sửa">
        <Button size="small" icon={<EditOutlined />} onClick={() => handleEdit(record)} />
      </Tooltip>
    ),
    canDelete && (
      <Popconfirm key="del" title="Xóa công ty này?" onConfirm={() => handleDelete(record)}>
        <Tooltip title="Xóa">
          <Button size="small" danger icon={<DeleteOutlined />} />
        </Tooltip>
      </Popconfirm>
    ),
  ].filter(Boolean) as ReactNode[];

  // Modal tạo/sửa — định nghĩa MỘT lần, render chung cho cả mobile và desktop.
  const formModal = (
    <Modal
      open={open}
      title={editingId ? 'Sửa công ty' : 'Tạo công ty' + (selectedParentId ? ' con' : '')}
      onOk={form.submit}
      onCancel={handleClose}
      destroyOnHidden
    >
      <Form form={form} layout="vertical" onFinish={save}>
        <Form.Item label="Tên công ty" name="name" rules={[{ required: true, message: 'Vui lòng nhập tên' }]}>
          <Input placeholder="Tên công ty" />
        </Form.Item>
        <Form.Item label="Mã công ty" name="code" rules={[
          { max: 20, message: 'Mã tối đa 20 ký tự' },
          { pattern: /^[A-Za-z0-9]+$/, message: 'Chỉ chấp nhận chữ/số, không dấu' },
          { validator: (_, v) => v && v.toUpperCase() === 'NOCO' ? Promise.reject('"NOCO" là mã dành riêng cho tài sản không thuộc công ty.') : Promise.resolve() },
        ]} extra="Dùng trong mã tự sinh của tài sản. Để trống sẽ tự gợi ý từ tên công ty.">
          <Input placeholder="VD: ABC" style={{ textTransform: 'uppercase' }} />
        </Form.Item>
        <Form.Item label="Công ty cha" name="parentId">
          <TreeSelect
            treeData={toTreeSelect(tree)}
            placeholder="Chọn công ty cha (Để trống nếu là gốc)"
            allowClear
            treeDefaultExpandAll
            disabled={!!selectedParentId}
          />
        </Form.Item>
      </Form>
    </Modal>
  );

  const columns: ProColumns<CompanyNode>[] = [
    { title: 'Mã công ty', dataIndex: 'code', key: 'code', width: 120, render: (_, r) => r.code || '-' },
    { title: 'Tên công ty', dataIndex: 'name', key: 'name' },
    {
      title: 'Hành động',
      key: 'actions',
      valueType: 'option' as const,
      width: 180,
      render: (_, record) => <Space size="small">{renderActions(record)}</Space>,
    },
  ];

  // ─── Mobile (ST7b): ProList Card thay Table — cùng fetch + cùng renderActions ───
  if (isMobile) {
    return (
      <div>
        <ProList<{ node: CompanyNode; isChild: boolean }>
          headerTitle="Danh sách công ty"
          actionRef={actionRef}
          rowKey={(r) => r.node.id}
          ghost
          cardProps={false}
          search={false}
          grid={{ gutter: 16, xs: 1, sm: 1 }}
          toolBarRender={() => [
            canCreate && (
              <Button key="add" type="primary" icon={<PlusOutlined />} onClick={() => handleAdd()}>
                Thêm
              </Button>
            ),
          ]}
          request={async () => {
            try {
              const t = await fetchTree();
              setTree(t);
              return { data: flattenCompanies(t), success: true, total: flattenCompanies(t).length };
            } catch {
              message.error('Lỗi tải danh sách công ty');
              return { data: [], success: false, total: 0 };
            }
          }}
          pagination={false}
          itemRender={({ node, isChild }) => (
            <Card hoverable style={{ borderRadius: 12, marginBottom: 16 }} styles={{ body: { padding: 16 } }}>
              <div style={{ display: 'flex', alignItems: 'center', gap: 10, marginBottom: 10 }}>
                <Text strong style={{ fontSize: 15 }}>{node.name}</Text>
                {isChild ? <Tag style={{ marginInlineEnd: 0 }} color="blue">Công ty con</Tag> : null}
                {node.code && !isChild ? <Tag style={{ marginInlineEnd: 0 }}>{node.code}</Tag> : null}
              </div>
              <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: '6px 12px', padding: '10px 12px', background: '#fafafa', borderRadius: 8 }}>
                <Text type="secondary" style={{ fontSize: 12 }}>Mã công ty</Text>
                <Text style={{ fontSize: 13 }}>{node.code || '-'}</Text>
              </div>
              <Divider style={{ margin: '10px 0' }} />
              <Space size="small" wrap>{renderActions(node)}</Space>
            </Card>
          )}
        />
        {formModal}
      </div>
    );
  }

  return (
    <div>
      <ProTable<CompanyNode>
        headerTitle="Danh sách công ty"
        rowKey="id"
        size="small"
        columns={columns}
        actionRef={actionRef}
        search={false}
        options={{ reload: true, density: true, setting: true }}
        toolBarRender={() => [
          canCreate && (
            <Button type="primary" icon={<PlusOutlined />} onClick={() => handleAdd()}>
              Tạo công ty
            </Button>
          ),
        ]}
        request={async () => {
          try {
            const t = await fetchTree();
            return { data: t, success: true, total: t.length };
          } catch {
            message.error('Lỗi tải danh sách công ty');
            return { data: [], success: false, total: 0 };
          }
        }}
        onLoad={(dataSource) => setTree(dataSource as CompanyNode[])}
        expandable={{ defaultExpandAllRows: true }}
        pagination={false}
        scroll={{ x: 'max-content' }}
      />

      {formModal}
    </div>
  );
}
