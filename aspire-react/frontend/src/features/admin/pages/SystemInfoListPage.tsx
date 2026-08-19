import { useState, useRef } from 'react';
import { Table, Button, Space, Modal, Form, Input, message, Popconfirm, Tooltip } from 'antd';
import { PlusOutlined, EditOutlined, DeleteOutlined } from '@ant-design/icons';
import { ProTable } from '@ant-design/pro-components';
import type { ActionType, ProColumns } from '@ant-design/pro-components';
import { Link } from 'react-router-dom';
import apiClient from '../../../services/api-client';
import { usePermission } from '../../../hooks/usePermission';
import CompanyTreeSelect from '../../../components/common/CompanyTreeSelect';

interface SystemPositionDto {
  id: string;
  systemInfoId: string;
  code: string;
  name: string;
  description?: string | null;
}

interface SystemInfoDto {
  id: string;
  code: string;
  name: string;
  description?: string | null;
  companyId?: string | null;
  company?: { id: string; name: string } | null;
  positions: SystemPositionDto[];
}

const CODE_PATTERN = /^[A-Z0-9]{3}-[A-Z0-9]{3}-[A-Z0-9]{3}$/;

export default function SystemInfoListPage() {
  const [open, setOpen] = useState(false);
  const [editingId, setEditingId] = useState<string | null>(null);
  const [form] = Form.useForm();
  const actionRef = useRef<ActionType | null>(null);

  // ST6b — permission gating (mirrors backend [Authorize(Policy=...)]).
  const canCreate = usePermission('systems.create');
  const canEdit = usePermission('systems.edit');
  const canDelete = usePermission('systems.delete');

  // Position sub-modal
  const [posOpen, setPosOpen] = useState(false);
  const [posEditingId, setPosEditingId] = useState<string | null>(null);
  const [posSystemInfoId, setPosSystemInfoId] = useState<string | null>(null);
  const [posForm] = Form.useForm();

  // === System Info handlers ===
  const handleAddSys = () => { setEditingId(null); form.resetFields(); setOpen(true); };
  const handleEditSys = (r: SystemInfoDto) => { setEditingId(r.id); form.setFieldsValue(r); setOpen(true); };
  const handleCloseSys = () => { setEditingId(null); form.resetFields(); setOpen(false); };

  const handleDeleteSys = async (record: SystemInfoDto) => {
    try { await apiClient.delete(`/system-infos/${record.id}`); message.success('Đã xóa'); actionRef.current?.reload(); }
    catch (err: any) { message.error(err?.response?.data?.message || 'Không thể xóa'); }
  };

  const saveSys = async () => {
    try {
      const values = await form.validateFields();
      // Auto-uppercase code
      values.code = values.code.toUpperCase();
      if (editingId) { await apiClient.put(`/system-infos/${editingId}`, values); message.success('Đã cập nhật'); }
      else { await apiClient.post('/system-infos', values); message.success('Đã tạo hệ thống'); }
      handleCloseSys(); actionRef.current?.reload();
    } catch (err: any) {
      if (err?.errorFields) return;
      message.error(err?.response?.data?.message || 'Lỗi lưu');
    }
  };

  // === Position handlers ===
  const handleAddPos = (systemInfoId: string) => {
    setPosSystemInfoId(systemInfoId);
    setPosEditingId(null);
    posForm.resetFields();
    setPosOpen(true);
  };

  const handleEditPos = (pos: SystemPositionDto) => {
    setPosSystemInfoId(pos.systemInfoId);
    setPosEditingId(pos.id);
    posForm.setFieldsValue(pos);
    setPosOpen(true);
  };

  const handleClosePos = () => {
    setPosSystemInfoId(null); setPosEditingId(null);
    posForm.resetFields(); setPosOpen(false);
  };

  const handleDeletePos = async (pos: SystemPositionDto) => {
    try { await apiClient.delete(`/system-infos/${pos.systemInfoId}/positions/${pos.id}`); message.success('Đã xóa'); actionRef.current?.reload(); }
    catch (err: any) { message.error(err?.response?.data?.message || 'Không thể xóa'); }
  };

  const savePos = async () => {
    try {
      const values = await posForm.validateFields();
      values.code = values.code.toUpperCase();
      if (posEditingId) {
        await apiClient.put(`/system-infos/${posSystemInfoId}/positions/${posEditingId}`, values);
        message.success('Đã cập nhật vị trí');
      } else {
        await apiClient.post(`/system-infos/${posSystemInfoId}/positions`, values);
        message.success('Đã thêm vị trí');
      }
      handleClosePos(); actionRef.current?.reload();
    } catch (err: any) {
      if (err?.errorFields) return;
      message.error(err?.response?.data?.message || 'Lỗi lưu vị trí');
    }
  };

  // System columns
  const sysCols: ProColumns<SystemInfoDto>[] = [
    {
      title: 'Mã hệ thống', dataIndex: 'code', key: 'code', width: 180,
      render: (_, r) => <Link to={`/systems/${r.id}`}>{r.code}</Link>,
    },
    {
      title: 'Tên hệ thống', dataIndex: 'name', key: 'name',
      render: (_: unknown, r: SystemInfoDto) => <Link to={`/systems/${r.id}`}>{r.name}</Link>,
    },
    { title: 'Mô tả', dataIndex: 'description', key: 'description', render: (_, r) => r.description || '-' },
    { title: 'Công ty', key: 'company', render: (_, r) => r.company?.name || '-' },
    {
      title: 'Hành động', key: 'actions', valueType: 'option' as const, width: 200,
      render: (_, record) => (
        <Space size="small">
          {canCreate && (
            <Tooltip title="Thêm">
            <Button size="small" icon={<PlusOutlined />} onClick={() => handleAddPos(record.id)}></Button>
            </Tooltip>)}
          {canEdit && (
            <Tooltip title="Sửa">
            <Button size="small" icon={<EditOutlined />} onClick={() => handleEditSys(record)}>
            </Button>
            </Tooltip>)}
          {canDelete && (
            <Popconfirm title="Xóa?" onConfirm={() => handleDeleteSys(record)}>
             <Tooltip title="Xóa"> <Button size="small" danger icon={<DeleteOutlined />}></Button></Tooltip>
            </Popconfirm>
          )}
        </Space>
      ),
    },
  ];

  // Position columns (expanded)
  const expandedRowRender = (record: SystemInfoDto) => {
    const posCols = [
      { title: 'Mã vị trí', dataIndex: 'code', key: 'code', width: 180 },
      { title: 'Tên vị trí', dataIndex: 'name', key: 'name' },
      { title: 'Mô tả', dataIndex: 'description', key: 'description', render: (v: string) => v || '-' },
      {
        title: 'Hành động', key: 'actions', width: 150,
        render: (_: any, pos: SystemPositionDto) => (
          <Space size="small">
            {canEdit && 
            <Tooltip title="Sửa">
            <Button size="small" icon={<EditOutlined />} onClick={() => handleEditPos(pos)}>
            </Button>
            </Tooltip>}
            {canDelete && (
              <Popconfirm title="Xóa?" onConfirm={() => handleDeletePos(pos)}>
                <Tooltip title="Xóa"><Button size="small" danger icon={<DeleteOutlined />}></Button></Tooltip>
              </Popconfirm>
            )}
          </Space>
        ),
      },
    ];
    return <Table columns={posCols} dataSource={record.positions} rowKey="id" pagination={false} size="small" />;
  };

  return (
    <div>
      <ProTable<SystemInfoDto>
        headerTitle="Danh sách hệ thống"
        rowKey="id"
        size="small"
        columns={sysCols}
        actionRef={actionRef}
        search={false}
        options={{ reload: true, density: true, setting: true }}
        toolBarRender={() => [
          canCreate && <Button type="primary" icon={<PlusOutlined />} onClick={handleAddSys}>Thêm hệ thống</Button>,
        ]}
        request={async () => {
          try {
            const r = await apiClient.get('/system-infos');
            return { data: r.data.data || [], success: true, total: (r.data.data || []).length };
          } catch {
            message.error('Lỗi tải danh sách hệ thống');
            return { data: [], success: false, total: 0 };
          }
        }}
        expandable={{ expandedRowRender, defaultExpandAllRows: false }}
       // scroll={{ x: 'max-content' }}
      />

      {/* System Info Modal */}
      <Modal open={open} title={editingId ? 'Sửa hệ thống' : 'Thêm hệ thống'} onOk={form.submit} onCancel={handleCloseSys} destroyOnHidden width={520}>
        <Form form={form} layout="vertical" onFinish={saveSys}>
          <Form.Item label="Mã hệ thống" name="code"
            rules={[
              { required: true, message: 'Vui lòng nhập mã' },
              { pattern: CODE_PATTERN, message: 'Định dạng: XXX-YYY-ZZZ (3 chữ/số, gạch nối, viết hoa)' },
            ]}
            getValueFromEvent={(e) => e.target.value.toUpperCase()}
          >
            <Input placeholder="VD: SYS-001-COR" maxLength={11} style={{ textTransform: 'uppercase' }} />
          </Form.Item>
          <Form.Item label="Tên hệ thống" name="name" rules={[{ required: true, message: 'Vui lòng nhập tên' }]}>
            <Input placeholder="Tên hệ thống" />
          </Form.Item>
          <Form.Item label="Công ty" name="companyId">
            <CompanyTreeSelect />
          </Form.Item>
          <Form.Item label="Mô tả" name="description">
            <Input.TextArea rows={2} />
          </Form.Item>
        </Form>
      </Modal>

      {/* Position Modal */}
      <Modal open={posOpen} title={posEditingId ? 'Sửa vị trí' : 'Thêm vị trí'} onOk={posForm.submit} onCancel={handleClosePos} destroyOnHidden width={520}>
        <Form form={posForm} layout="vertical" onFinish={savePos}>
          <Form.Item label="Mã vị trí" name="code"
            rules={[
              { required: true, message: 'Vui lòng nhập mã' },
              { pattern: CODE_PATTERN, message: 'Định dạng: XXX-YYY-ZZZ (3 chữ/số, gạch nối, viết hoa)' },
            ]}
            getValueFromEvent={(e) => e.target.value.toUpperCase()}
          >
            <Input placeholder="VD: POS-001-NOD" maxLength={11} style={{ textTransform: 'uppercase' }} />
          </Form.Item>
          <Form.Item label="Tên vị trí" name="name" rules={[{ required: true, message: 'Vui lòng nhập tên' }]}>
            <Input placeholder="Tên vị trí" />
          </Form.Item>
          <Form.Item label="Mô tả" name="description">
            <Input.TextArea rows={2} />
          </Form.Item>
        </Form>
      </Modal>
    </div>
  );
}
