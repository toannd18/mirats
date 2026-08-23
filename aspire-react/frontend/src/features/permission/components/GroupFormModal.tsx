import { useEffect, useMemo, useState } from 'react';
import {
  Alert, App, Button, Checkbox, Collapse, Form, Input, Modal, Spin, Tag, Typography,
} from 'antd';
import { LockOutlined } from '@ant-design/icons';
import { groupsApi } from '../services/groups.service';
import type { GroupDto, PermissionResourceGroup } from '../types/groups';
import { useIsMobile } from '../../../hooks/useIsMobile';

const { Text } = Typography;

/** Backend may serialize PermissionValue as int (1/-1/0) or enum string ("Grant"/"Deny"/"NotSet"). */
const toPermValue = (v: number | string): number =>
  typeof v === 'number' ? v : ({ Grant: 1, Deny: -1, NotSet: 0 } as Record<string, number>)[v] ?? 0;

interface GroupFormModalProps {
  open: boolean;
  /** null/undefined = create mode; otherwise edit the given group (list row data). */
  group: GroupDto | null;
  onClose: () => void;
  onSaved: () => void;
}

/**
 * Modal tạo/sửa nhóm (PermissionGroup) + phân quyền theo module (Resource).
 * Danh sách permission LẤY TỪ API `GET /api/v1/permissions` (catalog 76 key do backend
 * `PermissionCatalog` sinh ra) — KHÔNG hardcode như GroupFormPage cũ (thiếu ~30 key).
 */
export default function GroupFormModal({ open, group, onClose, onSaved }: GroupFormModalProps) {
  const { message } = App.useApp();
  const isMobile = useIsMobile();
  const [form] = Form.useForm();
  const isEdit = !!group;

  const [loading, setLoading] = useState(false);
  const [saving, setSaving] = useState(false);
  const [catalog, setCatalog] = useState<PermissionResourceGroup[]>([]);
  const [perms, setPerms] = useState<Record<string, number>>({});

  // Mỗi lần mở Modal: nạp catalog + permission hiện tại của group (nếu edit).
  useEffect(() => {
    if (!open) return;
    setLoading(true);
    setPerms({});
    setCatalog([]);
    (async () => {
      try {
        const res = await groupsApi.getCatalog();
        const data = (res.data?.data ?? []) as PermissionResourceGroup[];
        setCatalog(data);

        // Khởi tạo bản đồ permission key → value (mặc định NotSet = 0).
        const pmap: Record<string, number> = {};
        data.forEach(g => g.permissions.forEach(p => { pmap[p.code] = 0; }));

        if (group) {
          form.setFieldsValue({ name: group.name, description: group.description ?? '' });
          (group.permissions ?? []).forEach(p => {
            if (p.permissionKey in pmap) pmap[p.permissionKey] = toPermValue(p.value);
          });
        } else {
          form.resetFields();
        }
        setPerms(pmap);
      } catch {
        message.error('Không thể tải danh sách quyền');
      } finally {
        setLoading(false);
      }
    })();
  }, [open, group, form, message]);

  const toggle = (code: string) => {
    setPerms(prev => ({ ...prev, [code]: prev[code] === 1 ? 0 : 1 }));
  };

  const setAllInResource = (resource: string, checked: boolean) => {
    setPerms(prev => {
      const next = { ...prev };
      catalog.find(g => g.resource === resource)?.permissions.forEach(p => {
        next[p.code] = checked ? 1 : 0;
      });
      return next;
    });
  };

  const handleSubmit = async () => {
    try {
      const values = await form.validateFields();
      setSaving(true);
      const payload = {
        name: (values.name as string).trim(),
        description: ((values.description as string | undefined)?.trim() || null),
      };

      let groupId = group?.id;
      if (isEdit && groupId) {
        // Nhóm hệ thống không được đổi tên (backend trả SYSTEM_GROUP_LOCKED) → bỏ qua PUT name.
        if (!group.isSystem) {
          await groupsApi.update(groupId, payload);
        }
      } else {
        const res = await groupsApi.create(payload);
        groupId = res.data?.data?.id as string;
      }

      // Gửi đúng danh sách permission được cấp (value != 0) — backend thay thế toàn bộ.
      const entries = Object.entries(perms)
        .filter(([, v]) => v !== 0)
        .map(([permissionKey, value]) => ({ permissionKey, value }));
      await groupsApi.updatePermissions(groupId!, entries);

      message.success(isEdit ? 'Cập nhật nhóm thành công' : 'Tạo nhóm thành công');
      onSaved();
    } catch (err) {
      const msg = (err as { response?: { data?: { message?: string } } })?.response?.data?.message;
      if (msg) {
        message.error(msg);
      } else {
        message.error('Lỗi lưu dữ liệu');
      }
    } finally {
      setSaving(false);
    }
  };

  const grantedCount = Object.values(perms).filter(v => v === 1).length;
  const totalCount = Object.keys(perms).length;

  const panels = useMemo(
    () => catalog.map(g => {
      const resourcePerms = g.permissions;
      const allChecked = resourcePerms.length > 0 && resourcePerms.every(p => perms[p.code] === 1);
      const someChecked = resourcePerms.some(p => perms[p.code] === 1);
      const count = resourcePerms.filter(p => perms[p.code] === 1).length;
      return {
        key: g.resource,
        label: (
          <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: 8 }}>
            <span style={{ textTransform: 'capitalize' }}>{g.resource}</span>
            <Checkbox
              checked={allChecked}
              indeterminate={someChecked && !allChecked}
              onClick={e => e.stopPropagation()}
              onChange={e => setAllInResource(g.resource, e.target.checked)}
            >
              <Tag style={{ marginInlineEnd: 0 }}>{count}/{resourcePerms.length}</Tag>
            </Checkbox>
          </div>
        ),
        children: (
          <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fill, minmax(230px, 1fr))', gap: 4 }}>
            {resourcePerms.map(p => (
              <Checkbox key={p.code} checked={perms[p.code] === 1} onChange={() => toggle(p.code)}>
                <span title={p.description} style={{ fontSize: 12 }}>{p.code}</span>
              </Checkbox>
            ))}
          </div>
        ),
      };
    }),
    [catalog, perms], // eslint-disable-line react-hooks/exhaustive-deps
  );

  return (
    <Modal
      title={isEdit ? `Sửa nhóm: ${group?.name}` : 'Tạo nhóm mới'}
      open={open}
      onCancel={onClose}
      width={isMobile ? '95%' : 780}
      destroyOnHidden
      footer={[
        <Button key="cancel" onClick={onClose}>Hủy</Button>,
        <Button key="save" type="primary" loading={saving} onClick={() => void handleSubmit()}>Lưu</Button>,
      ]}
    >
      <Spin spinning={loading}>
        {isEdit && group?.isSystem && (
          <Alert
            type="info"
            showIcon
            style={{ marginBottom: 12 }}
            title="Nhóm hệ thống"
            description="Nhóm hệ thống không thể đổi tên hoặc xóa (bảo vệ chống tự khóa quyền), nhưng vẫn chỉnh sửa được danh sách quyền."
          />
        )}
        <Form form={form} layout="vertical">
          <Form.Item label="Tên nhóm" name="name" rules={[{ required: true, message: 'Nhập tên nhóm' }]}>
            <Input
              disabled={isEdit && !!group?.isSystem}
              placeholder="VD: Kế toán, Kỹ thuật..."
              maxLength={255}
              prefix={isEdit && group?.isSystem ? <LockOutlined /> : undefined}
            />
          </Form.Item>
          <Form.Item label="Mô tả" name="description">
            <Input.TextArea rows={2} maxLength={500} placeholder="Mô tả nhóm (tùy chọn)" />
          </Form.Item>
        </Form>

        <Text type="secondary" style={{ display: 'block', marginBottom: 8 }}>
          Phân quyền theo module — {grantedCount} / {totalCount} quyền được cấp
        </Text>
        <Collapse
          size="small"
          items={panels}
          defaultActiveKey={catalog.slice(0, 3).map(g => g.resource)}
        />
      </Spin>
    </Modal>
  );
}
