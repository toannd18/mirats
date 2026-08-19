import { useState, useRef } from 'react';
import { Button, Space, Modal, Form, Input, TreeSelect, message, Tooltip } from 'antd';
import { PlusOutlined, EditOutlined, DeleteOutlined } from '@ant-design/icons';
import { ProTable } from '@ant-design/pro-components';
import type { ActionType, ProColumns } from '@ant-design/pro-components';
import apiClient from '../../../services/api-client';
import { usePermission } from '../../../hooks/usePermission';

interface CompanyNode {
  id: string;
  name: string;
  parentId: string | null;
  children: CompanyNode[];
}

export default function CompanyListPage() {
  const [tree, setTree] = useState<CompanyNode[]>([]);
  const [open, setOpen] = useState(false);
  const [editingId, setEditingId] = useState<string | null>(null);
  const [selectedParentId, setSelectedParentId] = useState<string | null>(null);
  const [form] = Form.useForm();
  const actionRef = useRef<ActionType | null>(null);

  // ST6b — permission gating (mirrors backend [Authorize(Policy=...)]).
  const canCreate = usePermission('companies.create');
  const canEdit = usePermission('companies.edit');
  const canDelete = usePermission('companies.delete');

  /**
   * Convert tree data to TreeSelect format.
   * BUSINESS RULE: Only 2 levels allowed (Root → Child).
   * Child companies (level 2) are disabled in the TreeSelect
   * so users cannot select them as a parent.
   * We pass isRoot=true at the top level; children of children are not rendered.
   */
  const toTreeSelect = (nodes: CompanyNode[], isRoot = true): any[] =>
    nodes.map(n => {
      // A node is a child (level 2) if isRoot is false
      const isChild = !isRoot;
      return {
        title: n.name,
        value: n.id,
        // Disable child companies — they cannot be selected as parent
        disabled: isChild,
        // Only recurse into children of root nodes (level 1 children become level 2)
        children: n.children?.length ? toTreeSelect(n.children, false) : undefined,
      };
    });

  const handleEdit = (record: CompanyNode) => {
    setEditingId(record.id);
    setSelectedParentId(null);
    form.setFieldsValue({ name: record.name, parentId: record.parentId });
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

  const handleDelete = (record: CompanyNode) => {
    Modal.confirm({
      title: 'Xóa công ty?',
      content: `Bạn có chắc muốn xóa "${record.name}"?`,
      onOk: async () => {
        try {
          await apiClient.delete(`/companies/${record.id}`);
          message.success('Đã xóa');
          actionRef.current?.reload();
        } catch (err: any) {
          message.error(err?.response?.data?.message || 'Không thể xóa');
        }
      },
    });
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
    } catch (err: any) {
      if (err?.errorFields) return;
      message.error(err?.response?.data?.message || 'Lỗi lưu');
    }
  };

  const columns: ProColumns<CompanyNode>[] = [
    { title: 'Tên công ty', dataIndex: 'name', key: 'name' },
    {
      title: 'Hành động',
      key: 'actions',
      valueType: 'option' as const,
      width: 180,
      render: (_, record) => (
        <Space size="small">
          {/* BUSINESS RULE: Only Root companies (parentId == null) can have child companies */}
          {canCreate && record.parentId == null && (
            <Tooltip title="Thêm">
            <Button size="small" icon={<PlusOutlined />} onClick={() => handleAdd(record.id)}>
            </Button>
            </Tooltip>
          )}
          {canEdit && <Tooltip title="Sửa"> <Button size="small" icon={<EditOutlined />} onClick={() => handleEdit(record)} /> </Tooltip>}
          {canDelete && <Tooltip title="Xóa"><Button size="small" danger icon={<DeleteOutlined />} onClick={() => handleDelete(record)} /></Tooltip>}
        </Space>
      ),
    },
  ];

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
            const r = await apiClient.get('/companies');
            return { data: r.data.data || [], success: true, total: (r.data.data || []).length };
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
          <Form.Item label="Công ty cha" name="parentId">
            <TreeSelect
              treeData={toTreeSelect(tree)}
              placeholder="Chọn công ty cha (Để trống nếu là gốc)"
              allowClear
              treeDefaultExpandAll
              // When adding a child from a specific row, disable the TreeSelect
              // so the user cannot change the parent to something else
              disabled={!!selectedParentId}
            />
          </Form.Item>
        </Form>
      </Modal>
    </div>
  );
}
