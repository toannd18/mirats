import { useState, useRef } from 'react';
import { Table, Button, Form, Input, App, Space, Card, Tag, Typography, Empty, Divider } from 'antd';
import type { MenuProps, TableColumnsType } from 'antd';
import { PlusOutlined, EditOutlined, DeleteOutlined } from '@ant-design/icons';
import { ProList, ProTable } from '@ant-design/pro-components';
import type { ActionType, ProColumns } from '@ant-design/pro-components';
import { Link, useNavigate } from 'react-router-dom';
import { withOptionalFkCleared } from '../../../shared/utils/fk';
import apiClient from '../../../services/api-client';
import { usePermission } from '../../../hooks/usePermission';
import { useIsMobile } from '../../../hooks/useIsMobile';
import CompanyTreeSelect from '../../../components/common/CompanyTreeSelect';
import ListRowActions from '../../../shared/components/list/ListRowActions';
import type { ListPrimaryAction } from '../../../shared/components/list/ListRowActions';
import { NowrapText, EllipsisText } from '../../../shared/components/list/ListText';
import { useListColumnsState } from '../../../shared/hooks/useListColumnsState';
import ModalFormShell from '../../../shared/components/ModalFormShell';
import { applyServerFieldErrors, serverErrorMessage } from '../../../shared/utils/formErrors';

const { Text } = Typography;

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

const CODE_PATTERN = /^[A-Z]{3,4}-\d{4}-\d{3}$/;

// Versioned columns state (v2) — default visible: Mã, Tên, Công ty, Số vị trí (+ Thao tác).
// Only columns backed by real projected data exist: SystemInfo has NO CreatedAt/UpdatedAt, so the
// "Ngày tạo"/"Cập nhật" columns were removed instead of rendering a permanent '-'.
// Module-level constant keeps the object identity stable for useListColumnsState.
const COLUMN_DEFAULTS: Record<string, { show: boolean }> = {
  code: { show: true },
  name: { show: true },
  company: { show: true },
  positionCount: { show: true },
  description: { show: false },
  actions: { show: true },
};

// Natural (human) ordering: SYS-2026-2 < SYS-2026-10 is applied by the API (PostgreSQL ICU
// collation) — this page never sorts locally, it only forwards sortBy/sortDir.

// Stable rowKey (module scope avoids new function identity on every render).
const rowKey = (record: { id: string }) => record.id;

export default function SystemInfoListPage() {
  // [FE-R6] message/modal lấy từ App.useApp() (context theme) thay vì static import.
  const { message, modal } = App.useApp();
  const navigate = useNavigate();
  const isMobile = useIsMobile();
  const [open, setOpen] = useState(false);
  const [editingId, setEditingId] = useState<string | null>(null);
  const [form] = Form.useForm();
  // 1 actionRef dùng chung cho ProTable (desktop) và ProList (mobile Card).
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
    catch (err: unknown) { const e = err as { response?: { data?: { message?: string } } }; message.error(e?.response?.data?.message || 'Không thể xóa'); }
  };

  const confirmDeleteSys = (record: SystemInfoDto) => {
    modal.confirm({
      title: 'Xóa hệ thống?',
      content: `Hệ thống “${record.name}” sẽ bị xóa. Thao tác này không thể hoàn tác.`,
      okText: 'Xóa',
      cancelText: 'Hủy',
      okButtonProps: { danger: true },
      onOk: () => handleDeleteSys(record),
    });
  };

  const saveSys = async () => {
    try {
      const values = await form.validateFields();
      // Auto-uppercase code
      values.code = values.code.toUpperCase();
      if (editingId) {
        // [FIX-GUID-EMPTY phần 2] Ô công ty bị xóa ⇒ Guid.Empty để backend XÓA liên kết (chuẩn API.md).
        const payload = { ...values, ...withOptionalFkCleared(values, ['companyId']) };
        await apiClient.put(`/system-infos/${editingId}`, payload);
        message.success('Đã cập nhật');
      }
      else { await apiClient.post('/system-infos', values); message.success('Đã tạo hệ thống'); }
      handleCloseSys(); actionRef.current?.reload();
    } catch (err: unknown) {
      const e = err as { errorFields?: unknown; response?: { data?: { message?: string } } };
      if (e?.errorFields) return;
      if (!applyServerFieldErrors(form, err)) {
        message.error(serverErrorMessage(err, 'Không thể lưu hệ thống. Vui lòng thử lại.'));
      }
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
    catch (err: unknown) { const e = err as { response?: { data?: { message?: string } } }; message.error(e?.response?.data?.message || 'Không thể xóa'); }
  };

  const confirmDeletePos = (pos: SystemPositionDto) => {
    modal.confirm({
      title: 'Xóa vị trí?',
      content: `Vị trí “${pos.name}” sẽ bị xóa khỏi hệ thống.`,
      okText: 'Xóa',
      cancelText: 'Hủy',
      okButtonProps: { danger: true },
      onOk: () => handleDeletePos(pos),
    });
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
    } catch (err: unknown) {
      const e = err as { errorFields?: unknown; response?: { data?: { message?: string } } };
      if (e?.errorFields) return;
      if (!applyServerFieldErrors(posForm, err)) {
        message.error(serverErrorMessage(err, 'Không thể lưu vị trí. Vui lòng thử lại.'));
      }
    }
  };

  // === Row actions (UI-1 pattern): ONE primary action + ⋯ menu, shared by desktop & mobile ===
  // The ⋯ menu NEVER repeats the primary button: primary = Sửa, so the menu carries only
  // Thêm vị trí / Xóa.
  const primaryActionFor = (record: SystemInfoDto): ListPrimaryAction | null =>
    canEdit ? { key: 'edit', label: 'Sửa', icon: <EditOutlined />, onClick: () => handleEditSys(record) } : null;

  const menuItemsFor = (record: SystemInfoDto): MenuProps['items'] => {
    const items: NonNullable<MenuProps['items']> = [];
    if (canCreate) {
      items.push({ key: 'add-position', label: 'Thêm vị trí', icon: <PlusOutlined />, onClick: () => handleAddPos(record.id) });
    }
    if (canDelete) {
      items.push({ key: 'delete', label: 'Xóa', icon: <DeleteOutlined />, danger: true, onClick: () => confirmDeleteSys(record) });
    }
    return items;
  };

  const renderRowActions = (record: SystemInfoDto, stopPropagation = false) => (
    <ListRowActions
      primary={primaryActionFor(record)}
      menuItems={menuItemsFor(record)}
      stopPropagation={stopPropagation}
    />
  );

  // === Shared fetch (desktop ProTable + mobile ProList): GET /system-infos returns the full list ===
  // [SERVER-SIDE SORT] The API owns the ordering (`code` natural or `name`, via sortBy/sortDir).
  // This page NEVER sorts client-side — column `sorter: true` only tells antd to report the click,
  // and the resulting sort params are forwarded to the API. Keyword filter stays local (the list
  // endpoint has no search parameter) and preserves the server order.
  const loadPage = async (
    params: Record<string, unknown>,
    sort?: Record<string, unknown>,
  ) => {
    try {
      const sortEntry = Object.entries(sort ?? {}).find(([, order]) => order === 'ascend' || order === 'descend');
      const sortBy = (sortEntry?.[0] ?? 'code') as string;
      const sortDir = sortEntry?.[1] === 'descend' ? 'desc' : 'asc';

      const res = await apiClient.get('/system-infos', { params: { sortBy, sortDir } });
      const all = (res.data.data ?? []) as SystemInfoDto[];

      const keyword = String(params.keyword ?? '').trim().toLowerCase();
      const rows = keyword
        ? all.filter((r) => [r.code, r.name, r.company?.name]
          .some((v) => (v ?? '').toLowerCase().includes(keyword)))
        : all;

      return { data: rows, success: true, total: rows.length };
    } catch {
      message.error('Lỗi tải danh sách hệ thống');
      return { data: [], success: false, total: 0 };
    }
  };

  const openDetail = (record: SystemInfoDto) => navigate(`/systems/${record.id}`);

  // Search (client-side keyword) — mobile form is collapsed by default.
  const searchColumns: ProColumns<SystemInfoDto>[] = [
    {
      title: 'Tìm kiếm', dataIndex: 'keyword', valueType: 'text', hideInTable: true,
      fieldProps: { placeholder: 'Mã, tên hệ thống hoặc công ty...', allowClear: true },
    },
  ];

  const columnsState = useListColumnsState('system-list-columns-v2', COLUMN_DEFAULTS);

  // Desktop columns — `sorter: true` (server-side) ONLY on the whitelisted fields (code/name).
  // No other column offers sorting, and no column carries a local comparator.
  const desktopColumns: ProColumns<SystemInfoDto>[] = ([
    {
      title: 'Mã', dataIndex: 'code', key: 'code', width: 150,
      sorter: true,
      render: (_: unknown, record: SystemInfoDto) => (
        <Link to={`/systems/${record.id}`}>
          <NowrapText value={record.code} code />
        </Link>
      ),
    },
    {
      title: 'Tên', dataIndex: 'name', key: 'name', width: 240,
      sorter: true,
      render: (_: unknown, record: SystemInfoDto) => (
        <Link to={`/systems/${record.id}`} style={{ display: 'block', minWidth: 0 }}>
          <EllipsisText value={record.name} />
        </Link>
      ),
    },
    {
      title: 'Công ty', key: 'company', width: 180,
      render: (_: unknown, record: SystemInfoDto) => <EllipsisText value={record.company?.name} />,
    },
    {
      title: 'Số vị trí', key: 'positionCount', width: 110, align: 'center',
      render: (_: unknown, record: SystemInfoDto) => record.positions?.length ?? 0,
    },
    {
      title: 'Mô tả', dataIndex: 'description', key: 'description', width: 280,
      render: (_: unknown, record: SystemInfoDto) => <EllipsisText value={record.description} />,
    },
    {
      title: 'Thao tác', key: 'actions', width: 150, fixed: 'right',
      render: (_: unknown, record: SystemInfoDto) => renderRowActions(record),
    },
  ] as ProColumns<SystemInfoDto>[]).map((column) => ({ ...column, hideInSearch: true, search: false }));

  // Position columns (expanded row) — same one-primary-action + menu pattern.
  const expandedRowRender = (record: SystemInfoDto) => {
    const posCols: TableColumnsType<SystemPositionDto> = [
      {
        title: 'Mã vị trí', dataIndex: 'code', key: 'code', width: 160,
        render: (_: unknown, pos: SystemPositionDto) => <NowrapText value={pos.code} code />,
      },
      {
        title: 'Tên vị trí', dataIndex: 'name', key: 'name',
        render: (_: unknown, pos: SystemPositionDto) => <EllipsisText value={pos.name} />,
      },
      {
        title: 'Mô tả', dataIndex: 'description', key: 'description', width: 280,
        render: (_: unknown, pos: SystemPositionDto) => <EllipsisText value={pos.description} />,
      },
      {
        title: 'Thao tác', key: 'actions', width: 150,
        render: (_: unknown, pos: SystemPositionDto) => (
          <ListRowActions
            primary={canEdit ? { key: 'edit', label: 'Sửa', icon: <EditOutlined />, onClick: () => handleEditPos(pos) } : null}
            menuItems={canDelete
              ? [{ key: 'delete', label: 'Xóa', icon: <DeleteOutlined />, danger: true, onClick: () => confirmDeletePos(pos) }]
              : []}
          />
        ),
      },
    ];
    return (
      <Table
        columns={posCols}
        dataSource={record.positions}
        rowKey="id"
        pagination={false}
        size="small"
        scroll={{ x: 'max-content' }}
        locale={{ emptyText: 'Hệ thống chưa có vị trí nào' }}
      />
    );
  };

  // Modals — defined ONCE, rendered by both mobile and desktop branches.
  const systemModal = (
    <ModalFormShell
      open={open}
      title={editingId ? 'Sửa hệ thống' : 'Thêm hệ thống'}
      onOk={() => form.submit()}
      onCancel={handleCloseSys}
      okText={editingId ? 'Lưu thay đổi' : 'Tạo hệ thống'}
      size="sm"
    >
      <Form form={form} layout="vertical" onFinish={saveSys}>
        <Form.Item label="Mã hệ thống" name="code"
          rules={[
            { required: true, message: 'Vui lòng nhập mã' },
            { pattern: CODE_PATTERN, message: 'Định dạng: XXX(X)-YYYY-ZZZ (3-4 chữ hoa - 4 số năm - 3 số thứ tự)' },
          ]}
          getValueFromEvent={(e) => e.target.value.toUpperCase()}
        >
          <Input placeholder="VD: SYS-2026-001" maxLength={13} style={{ textTransform: 'uppercase' }} />
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
    </ModalFormShell>
  );

  const positionModal = (
    <ModalFormShell
      open={posOpen}
      title={posEditingId ? 'Sửa vị trí' : 'Thêm vị trí'}
      onOk={() => posForm.submit()}
      onCancel={handleClosePos}
      okText={posEditingId ? 'Lưu thay đổi' : 'Tạo vị trí'}
      size="sm"
    >
      <Form form={posForm} layout="vertical" onFinish={savePos}>
        <Form.Item label="Mã vị trí" name="code"
          rules={[
            { required: true, message: 'Vui lòng nhập mã' },
            { pattern: CODE_PATTERN, message: 'Định dạng: XXX(X)-YYYY-ZZZ (3-4 chữ hoa - 4 số năm - 3 số thứ tự)' },
          ]}
          getValueFromEvent={(e) => e.target.value.toUpperCase()}
        >
          <Input placeholder="VD: POS-2026-001" maxLength={13} style={{ textTransform: 'uppercase' }} />
        </Form.Item>
        <Form.Item label="Tên vị trí" name="name" rules={[{ required: true, message: 'Vui lòng nhập tên' }]}>
          <Input placeholder="Tên vị trí" />
        </Form.Item>
        <Form.Item label="Mô tả" name="description">
          <Input.TextArea rows={2} />
        </Form.Item>
      </Form>
    </ModalFormShell>
  );

  // ─── Mobile: ProList Card (filters collapsed by default, no Mô tả in the card) ───
  if (isMobile) {
    return (
      <div>
        <ProList<SystemInfoDto>
          headerTitle="Danh sách hệ thống"
          actionRef={actionRef}
          rowKey={rowKey}
          ghost
          cardProps={false}
          columns={searchColumns}
          grid={{ gutter: 16, xs: 1, sm: 1 }}
          search={{ labelWidth: 'auto', defaultCollapsed: true }}
          toolBarRender={() => [
            canCreate && <Button key="create" type="primary" icon={<PlusOutlined />} onClick={handleAddSys}>Thêm hệ thống</Button>,
          ]}
          request={async (params, sort) => loadPage(
            params as Record<string, unknown>,
            sort as Record<string, unknown>,
          )}
          pagination={{
            defaultPageSize: 12, showSizeChanger: false,
            showTotal: (total, range) => `${range[0]}-${range[1]} / ${total} hệ thống`,
          }}
          locale={{ emptyText: <Empty description="Không tìm thấy hệ thống phù hợp" /> }}
          itemRender={(record) => (
            <Card hoverable style={{ borderRadius: 12, marginBottom: 16, cursor: 'pointer' }}
              onClick={() => openDetail(record)} styles={{ body: { padding: 16 } }}>
              <div style={{ display: 'flex', alignItems: 'baseline', gap: 8, flexWrap: 'wrap', marginBottom: 8 }}>
                <Link to={`/systems/${record.id}`} style={{ fontSize: 15, fontWeight: 600 }}
                  onClick={(e) => e.stopPropagation()}>
                  {record.name}
                </Link>
                <Tag style={{ marginInlineEnd: 0 }}>{record.code}</Tag>
                {record.company?.name && <Tag color="blue" style={{ marginInlineEnd: 0 }}>{record.company.name}</Tag>}
              </div>
              <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: '6px 12px', padding: '10px 12px', background: '#fafafa', borderRadius: 8 }}>
                <Text type="secondary" style={{ fontSize: 12 }}>Công ty</Text>
                <Text style={{ fontSize: 13 }}>{record.company?.name || '-'}</Text>
                <Text type="secondary" style={{ fontSize: 12 }}>Số vị trí</Text>
                <Text style={{ fontSize: 13 }}>{record.positions?.length ?? 0}</Text>
              </div>
              <Divider style={{ margin: '10px 0' }} />
              <Space size="small" wrap>{renderRowActions(record, true)}</Space>
            </Card>
          )}
        />
        {systemModal}
        {positionModal}
      </div>
    );
  }

  return (
    <div>
      <ProTable<SystemInfoDto>
        headerTitle="Danh sách hệ thống"
        rowKey={rowKey}
        size="small"
        columns={[...searchColumns, ...desktopColumns]}
        actionRef={actionRef}
        search={{ labelWidth: 'auto', defaultCollapsed: false }}
        options={{ reload: true, density: true, setting: true }}
        columnsState={columnsState}
        toolBarRender={() => [
          canCreate && <Button key="create" type="primary" icon={<PlusOutlined />} onClick={handleAddSys}>Thêm hệ thống</Button>,
        ]}
        request={async (params, sort) => loadPage(
          params as Record<string, unknown>,
          sort as Record<string, unknown>,
        )}
        pagination={{
          defaultPageSize: 12, showSizeChanger: true,
          showTotal: (total, range) => `${range[0]}-${range[1]} / ${total} hệ thống`,
        }}
        locale={{ emptyText: <Empty description="Không tìm thấy hệ thống phù hợp" /> }}
        expandable={{ expandedRowRender, defaultExpandAllRows: false }}
        scroll={{ x: 'max-content' }}
      />
      {systemModal}
      {positionModal}
    </div>
  );
}
