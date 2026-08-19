import { useRef, useState } from 'react';
import { App, Button, Popconfirm, Space, Tag, Tooltip, Typography } from 'antd';
import { PlusOutlined, DeleteOutlined, EditOutlined } from '@ant-design/icons';
import { ProTable } from '@ant-design/pro-components';
import type { ActionType, ProColumns } from '@ant-design/pro-components';
import { groupsApi } from '../services/groups.service';
import type { GroupDto } from '../types/groups';
import { usePermission } from '../../../hooks/usePermission';
import GroupFormModal from '../components/GroupFormModal';

const { Text } = Typography;

export default function GroupListPage() {
  const { message } = App.useApp();
  const actionRef = useRef<ActionType>(null);

  const [modalOpen, setModalOpen] = useState(false);
  const [editingGroup, setEditingGroup] = useState<GroupDto | null>(null);

  // ST6b — GroupsController is protected by the "admin" policy (no per-action keys).
  const canAdmin = usePermission('admin');

  const handleCreate = () => {
    setEditingGroup(null);
    setModalOpen(true);
  };

  const handleEdit = (group: GroupDto) => {
    setEditingGroup(group);
    setModalOpen(true);
  };

  const handleDelete = async (id: string, name: string) => {
    try {
      await groupsApi.delete(id);
      void message.success(`Đã xóa nhóm "${name}"`);
      actionRef.current?.reload();
    } catch (err) {
      const msg = (err as { response?: { data?: { message?: string } } })?.response?.data?.message;
      void message.error(msg ?? 'Không thể xóa nhóm');
    }
  };

  const handleModalSuccess = () => {
    setModalOpen(false);
    setEditingGroup(null);
    actionRef.current?.reload();
  };

  const columns: ProColumns<GroupDto>[] = [
    {
      title: 'Tên nhóm',
      dataIndex: 'name',
      width: 180,
      ellipsis: true,
      render: (_, record) => (
        <Space size={6}>
          <span>{record.name}</span>
          {record.isSystem && (
            <Tooltip title="Nhóm hệ thống — không xóa/đổi tên được">
              <Tag color="purple">Hệ thống</Tag>
            </Tooltip>
          )}
        </Space>
      ),
    },
    {
      title: 'Mô tả',
      dataIndex: 'description',
      ellipsis: true,
      render: (_, record) => record.description || '—',
    },
    {
      title: 'Thành viên',
      dataIndex: 'userCount',
      width: 100,
      align: 'center',
      render: (_, record) => <Tag>{record.userCount}</Tag>,
    },
    {
      title: 'Quyền',
      dataIndex: 'permissions',
      width: 320,
      ellipsis: true,
      render: (_, record) => {
        const toPermValue = (v: number | string): number =>
          typeof v === 'number' ? v : ({ Grant: 1, Deny: -1, NotSet: 0 } as Record<string, number>)[v] ?? 0;
        const granted = (record.permissions ?? []).filter(p => toPermValue(p.value) === 1);
        const shown = granted.slice(0, 4);
        const rest = granted.length - shown.length;
        return (
          <Space size={[4, 4]} wrap>
            {granted.length === 0 && <Text type="secondary">Không có quyền</Text>}
            {shown.map(p => (
              <Tag key={p.permissionKey} color="blue" style={{ marginInlineEnd: 0 }}>{p.permissionKey}</Tag>
            ))}
            {rest > 0 && <Tag style={{ marginInlineEnd: 0 }}>+{rest}</Tag>}
          </Space>
        );
      },
    },
    {
      title: 'Thao tác',
      valueType: 'option',
      width: 140,
      fixed: 'right',
      render: (_, record) => (
        <Space size="small">
          {canAdmin && (
            <Tooltip title={record.isSystem ? 'Sửa danh sách quyền' : 'Sửa nhóm'}>
              <Button
                type="link"
                size="small"
                icon={<EditOutlined />}
                onClick={() => handleEdit(record)}
              />
            </Tooltip>
          )}
          {canAdmin && (
            <Popconfirm
              title="Xóa nhóm này?"
              description={`Nhóm "${record.name}" sẽ bị xóa vĩnh viễn.`}
              onConfirm={() => void handleDelete(record.id, record.name)}
              okText="Xóa"
              okButtonProps={{ danger: true }}
              cancelText="Hủy"
              placement="left"
            >
              <Tooltip title={record.isSystem ? 'Nhóm hệ thống không thể xóa' : undefined}>
                <Button
                  type="link"
                  size="small"
                  danger
                  icon={<DeleteOutlined />}
                  disabled={record.isSystem}
                />
              </Tooltip>
            </Popconfirm>
          )}
        </Space>
      ),
    },
  ];

  return (
    <>
      <ProTable<GroupDto>
        headerTitle="Quản lý nhóm phân quyền"
        actionRef={actionRef}
        rowKey="id"
        size="small"
        cardBordered
        columnsState={{ persistenceKey: 'group-list-table', persistenceType: 'localStorage' }}
        search={false}
        options={false}
        toolBarRender={() => [
          canAdmin && (
            <Button
              key="create"
              type="primary"
              icon={<PlusOutlined />}
              onClick={handleCreate}
            >
              Tạo nhóm mới
            </Button>
          ),
        ]}
        request={async () => {
          try {
            const res = await groupsApi.list();
            return { data: res.data?.data ?? [], success: true, total: (res.data?.data ?? []).length };
          } catch {
            void message.error('Không thể tải danh sách nhóm');
            return { data: [], success: false, total: 0 };
          }
        }}
        columns={columns}
        pagination={false}
        scroll={{ x: 900 }}
      />

      <GroupFormModal
        key={editingGroup?.id ?? 'new'}
        open={modalOpen}
        group={editingGroup}
        onClose={() => {
          setModalOpen(false);
          setEditingGroup(null);
        }}
        onSaved={handleModalSuccess}
      />
    </>
  );
}
