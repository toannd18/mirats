import { useCallback, useEffect, useRef, useState } from 'react';
import { App, Button, Card, Divider, Select, Space, Tag, Tooltip, Typography } from 'antd';
import { SafetyOutlined, ExclamationCircleOutlined } from '@ant-design/icons';
import { ProList, ProTable } from '@ant-design/pro-components';
import type { ActionType, ProColumns } from '@ant-design/pro-components';
import apiClient from '../../../services/api-client';
import { groupsApi } from '../services/groups.service';
import type { PermissionResourceGroup } from '../types/groups';
import ModalFormShell from '../../../shared/components/ModalFormShell';
import { serverErrorMessage } from '../../../shared/utils/formErrors';
import { useIsMobile } from '../../../hooks/useIsMobile';

const { Text } = Typography;

interface GroupOption { id: string; name: string; }

interface MatrixUser {
  id: string;
  username: string;
  email: string;
  firstName: string;
  lastName: string;
  isSuperUser: boolean;
  groups?: { groupId: string; name: string; isSystem: boolean }[];
  userPermissions: { permissionKey: string; value: number }[];
  groupPermissions: { groupName: string; permissionKey: string; value: number }[];
}

const RESOURCE_LABELS: Record<string, string> = {
  assets: 'Tài sản',
  consumables: 'Vật tư tiêu hao',
  components: 'Linh kiện',
  accessories: 'Phụ kiện',
  licenses: 'Bản quyền',
  users: 'Người dùng',
  companies: 'Công ty',
  models: 'Model tài sản',
  categories: 'Danh mục',
  manufacturers: 'Nhà sản xuất',
  suppliers: 'Nhà cung cấp',
  departments: 'Phòng ban',
  locations: 'Vị trí',
  statuslabels: 'Trạng thái',
  depreciations: 'Khấu hao',
  reports: 'Báo cáo',
  customfields: 'Trường tùy chỉnh',
  systems: 'Hệ thống',
  maintenance: 'Bảo dưỡng',
  system: 'Hệ thống',
};

const ACTION_LABELS: Record<string, string> = {
  view: 'Xem',
  create: 'Tạo',
  edit: 'Sửa',
  delete: 'Xóa',
  checkout: 'Cấp phát',
  checkin: 'Thu hồi',
  audit: 'Kiểm kê',
  correct: 'Đính chính',
  templates: 'Mẫu bảo dưỡng',
  campaigns: 'Đợt bảo dưỡng',
  import: 'Nhập Excel',
  export: 'Xuất dữ liệu',
  admin: 'Quản trị',
  superuser: 'Toàn quyền',
  config: 'Cấu hình',
};

const resourceLabel = (resource: string) => RESOURCE_LABELS[resource] ?? resource;
const actionLabel = (action: string) => ACTION_LABELS[action] ?? action;

/** Tính permission hiệu dụng theo đúng semantic PermissionHandler: user Deny override, group grant chỉ lấp key chưa set. */
const computeEffective = (u: MatrixUser): Record<string, number> => {
  const eff: Record<string, number> = {};
  (u.userPermissions ?? []).forEach(p => { eff[p.permissionKey] = p.value; });
  (u.groupPermissions ?? []).forEach(p => {
    if (p.value === 1 && !(p.permissionKey in eff)) eff[p.permissionKey] = 1;
  });
  return eff;
};

interface PermissionSummaryProps {
  user: MatrixUser;
  catalog: PermissionResourceGroup[];
}

/** Vietnamese labels for resources/actions; permission codes remain the stable backend keys. */
function PermissionSummary({ user, catalog }: PermissionSummaryProps) {
  const eff = computeEffective(user);
  const grantedKeys = Object.entries(eff).filter(([, value]) => value === 1).map(([key]) => key);
  if (grantedKeys.length === 0) {
    return <Text type="secondary">Không có quyền nào được cấp.</Text>;
  }
  return (
    <Space orientation="vertical" size={6} style={{ width: '100%' }}>
      {catalog.map(group => {
        const keys = group.permissions
          .filter(permission => grantedKeys.includes(permission.code))
          .map(permission => actionLabel(permission.action));
        if (keys.length === 0) return null;
        return (
          <div key={group.resource}>
            <Text strong style={{ marginRight: 8 }}>{resourceLabel(group.resource)}:</Text>
            <Space size={[4, 4]} wrap>
              {keys.map((label, index) => (
                <Tag key={`${group.resource}-${label}-${index}`} color="blue" style={{ marginInlineEnd: 0 }}>
                  {label}
                </Tag>
              ))}
            </Space>
          </div>
        );
      })}
    </Space>
  );
}

export default function PermissionMatrixPage() {
  const { message } = App.useApp();
  const isMobile = useIsMobile();
  const actionRef = useRef<ActionType>(null);

  const [catalog, setCatalog] = useState<PermissionResourceGroup[]>([]);
  const [groupOptions, setGroupOptions] = useState<GroupOption[]>([]);

  // Gán nhóm cho user
  const [assignUser, setAssignUser] = useState<MatrixUser | null>(null);
  const [selectedGroupIds, setSelectedGroupIds] = useState<string[]>([]);
  const [saving, setSaving] = useState(false);

  useEffect(() => {
    void groupsApi.getCatalog().then(r => setCatalog((r.data?.data ?? []) as PermissionResourceGroup[])).catch(() => setCatalog([]));
    void groupsApi.list().then(r => {
      const rows = (r.data?.data ?? []) as { id: string; name: string }[];
      setGroupOptions(rows.map(g => ({ id: g.id, name: g.name })));
    }).catch(() => setGroupOptions([]));
  }, []);

  const openAssign = (user: MatrixUser) => {
    setAssignUser(user);
    setSelectedGroupIds((user.groups ?? []).map(g => g.groupId));
  };

  const handleAssignSave = async () => {
    if (!assignUser) return;
    setSaving(true);
    try {
      const res = await apiClient.put(`/users/${assignUser.id}/groups`, { groupIds: selectedGroupIds });
      message.success((res.data?.message as string) ?? 'Đã cập nhật nhóm cho người dùng');
      setAssignUser(null);
      actionRef.current?.reload();
    } catch (err) {
      message.error(serverErrorMessage(err, 'Không thể cập nhật nhóm.'));
    } finally {
      setSaving(false);
    }
  };

  const fetchUsers = useCallback(async () => {
    try {
      const [usersRes, matrixRes] = await Promise.all([
        apiClient.get('/users', { params: { pageSize: 500 } }),
        apiClient.get('/permissions/matrix'),
      ]);
      const listUsers = (usersRes.data?.data ?? []) as Partial<MatrixUser>[];
      const matrixUsers = (matrixRes.data?.data ?? []) as Partial<MatrixUser>[];
      const rows = listUsers.map(u => {
        const m = matrixUsers.find(x => x.id === u.id);
        return {
          ...u,
          userPermissions: m?.userPermissions ?? [],
          groupPermissions: m?.groupPermissions ?? [],
        } as MatrixUser;
      });
      return { data: rows, success: true, total: rows.length };
    } catch {
      void message.error('Không thể tải dữ liệu phân quyền');
      return { data: [], success: false, total: 0 };
    }
  }, [message]);

  const columns: ProColumns<MatrixUser>[] = [
    {
      title: 'Người dùng',
      dataIndex: 'username',
      width: 240,
      render: (_, record) => (
        <Space size={6}>
          <span>{[record.firstName, record.lastName].filter(Boolean).join(' ') || record.username}</span>
          <Text type="secondary" style={{ fontSize: 12 }}>@{record.username}</Text>
          {record.isSuperUser && <Tag color="purple">Toàn quyền</Tag>}
        </Space>
      ),
    },
    {
      title: 'Nhóm',
      dataIndex: 'groups',
      width: 280,
      render: (_, record) => {
        const groups = record.groups ?? [];
        return groups.length === 0
          ? <Text type="secondary">Chưa gán nhóm</Text>
          : (
            <Space size={[4, 4]} wrap>
              {groups.map(g => <Tag key={g.groupId} color={g.isSystem ? 'purple' : 'blue'} style={{ marginInlineEnd: 0 }}>{g.name}</Tag>)}
            </Space>
          );
      },
    },
    {
      title: 'Quyền được cấp',
      dataIndex: 'grantedCount',
      width: 160,
      align: 'center',
      render: (_, record) => {
        const eff = computeEffective(record);
        const granted = Object.values(eff).filter(v => v === 1).length;
        const denied = Object.values(eff).filter(v => v === -1).length;
        return (
          <Space size={4}>
            <Tag color="green">{granted} được cấp</Tag>
            {denied > 0 && <Tag color="red">{denied} chặn</Tag>}
          </Space>
        );
      },
    },
    {
      title: 'Thao tác',
      valueType: 'option',
      width: 130,
      fixed: 'right',
      render: (_, record) => (
        <Button type="link" size="small" icon={<SafetyOutlined />} onClick={() => openAssign(record)}>
          Gán nhóm
        </Button>
      ),
    },
  ];

  const headerTitle = 'Phân quyền theo nhóm — gán nhóm cho người dùng';
  const pagination = { pageSize: 10, showSizeChanger: true };

  return (
    <>
      {isMobile ? (
        <ProList<MatrixUser>
          rowKey="id"
          actionRef={actionRef}
          headerTitle={headerTitle}
          search={false}
          options={false}
          grid={{ gutter: 12, xs: 1, sm: 1 }}
          request={fetchUsers}
          pagination={{ pageSize: 10, showSizeChanger: false }}
          itemRender={(record) => {
            const eff = computeEffective(record);
            const granted = Object.values(eff).filter(v => v === 1).length;
            const denied = Object.values(eff).filter(v => v === -1).length;
            return (
              <Card size="small" style={{ marginBottom: 12, borderRadius: 10 }}>
                <Space style={{ width: '100%', justifyContent: 'space-between' }} wrap>
                  <Text strong>{[record.firstName, record.lastName].filter(Boolean).join(' ') || record.username}</Text>
                  {record.isSuperUser && <Tag color="purple">Toàn quyền</Tag>}
                </Space>
                <Text type="secondary" style={{ display: 'block', marginBottom: 6 }}>@{record.username}</Text>
                <Space size={[4, 4]} wrap style={{ marginBottom: 8 }}>
                  {(record.groups ?? []).length === 0
                    ? <Text type="secondary">Chưa gán nhóm</Text>
                    : (record.groups ?? []).map(group => (
                      <Tag key={group.groupId} color={group.isSystem ? 'purple' : 'blue'}>{group.name}</Tag>
                    ))}
                </Space>
                <div style={{ marginBottom: 8 }}>
                  <Tag color="green">{granted} được cấp</Tag>
                  {denied > 0 && <Tag color="red">{denied} chặn</Tag>}
                </div>
                <Divider style={{ margin: '8px 0' }} />
                <PermissionSummary user={record} catalog={catalog} />
                <Button type="link" size="small" icon={<SafetyOutlined />} style={{ paddingInline: 0, marginTop: 8 }} onClick={() => openAssign(record)}>
                  Gán nhóm
                </Button>
              </Card>
            );
          }}
        />
      ) : (
        <ProTable<MatrixUser>
          headerTitle={headerTitle}
          actionRef={actionRef}
          rowKey="id"
          size="small"
          cardBordered
          search={false}
          options={false}
          expandable={{
            expandedRowRender: (record) => <PermissionSummary user={record} catalog={catalog} />,
          }}
          request={fetchUsers}
          columns={columns}
          pagination={pagination}
          scroll={{ x: 'max-content' }}
        />
      )}

      <ModalFormShell
        title={assignUser ? `Gán nhóm cho: ${[assignUser.firstName, assignUser.lastName].filter(Boolean).join(' ') || assignUser.username}` : 'Gán nhóm'}
        open={!!assignUser}
        onCancel={() => setAssignUser(null)}
        onOk={() => void handleAssignSave()}
        confirmLoading={saving}
        okText="Lưu thay đổi"
        size="sm"
      >
        <p style={{ marginBottom: 8 }}>
          Chọn các nhóm quyền cho người dùng. Thay đổi ảnh hưởng tức thì tới các quyền được cấp.
        </p>
        <Select
          mode="multiple"
          style={{ width: '100%' }}
          placeholder="Chọn nhóm..."
          options={groupOptions.map(g => ({ label: g.name, value: g.id }))}
          value={selectedGroupIds}
          onChange={(v) => setSelectedGroupIds(v as string[])}
          optionFilterProp="label"
        />
        <div style={{ marginTop: 8, fontSize: 12 }}>
          <Tooltip title="Bạn không thể tự gỡ quyền quản trị của chính mình nếu là người cuối cùng giữ quyền quản trị.">
            <Text type="secondary"><ExclamationCircleOutlined style={{ marginInlineEnd: 4 }} />Chống tự khóa quyền: hệ thống chặn gỡ quyền quản trị cuối cùng của chính bạn.</Text>
          </Tooltip>
        </div>
      </ModalFormShell>
    </>
  );
}
