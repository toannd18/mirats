import { useCallback, useEffect, useMemo, useState } from 'react';
import { Link, useNavigate, useParams } from 'react-router-dom';
import {
  App, Button, Card, Descriptions, Divider, Empty, Form, Input, InputNumber, Modal, Segmented, Select, Space, Spin, Table, Tabs, Tag, Typography,
} from 'antd';
import {
  ArrowLeftOutlined, CarOutlined, ClusterOutlined, DeleteOutlined, EditOutlined, EllipsisOutlined, ExperimentOutlined, GiftOutlined, HistoryOutlined, KeyOutlined, LaptopOutlined, PlusOutlined, ToolOutlined,
} from '@ant-design/icons';
import { ProList, ProTable } from '@ant-design/pro-components';
import type { ProColumns } from '@ant-design/pro-components';
import apiClient from '../../../services/api-client';
import {
  systemsService, type SystemAccessoryDto, type SystemAssetDto, type SystemInfoDetailDto,
} from '../services/systems.service';
import ActionLogTable from '../../../shared/components/ActionLogTable';
import type { ActionLogRow } from '../../../shared/components/ActionLogTable';
import MaintenanceTable from '../../maintenance/components/MaintenanceTable';
import CampaignHistoryTable from '../../maintenance/components/CampaignHistoryTable';
import LicenseUsageTable from '../../../shared/components/LicenseUsageTable';
import { licensesApi } from '../../license/services/licenses.service';
import { ASSET_STATUS_COLORS, ASSET_STATUS_LABELS, type AssetStatus } from '../../asset/types/asset';
import { formatDate } from '../../../utils/format';
import { useIsMobile } from '../../../hooks/useIsMobile';
import { EllipsisText, NowrapText } from '../../../shared/components/list/ListText';
import ListRowActions from '../../../shared/components/list/ListRowActions';
import { usePermission } from '../../../hooks/usePermission';

const { Title, Text } = Typography;

const ASSIGNMENT_TARGET_LABELS: Record<string, string> = {
  User: 'Người dùng',
  Department: 'Phòng ban',
  SystemPosition: 'Hệ thống',
};

interface SystemSparePartDto {
  id: string;
  itemId: string;
  type: 'Component' | 'Consumable';
  name: string;
  code: string | null;
  remaining: number;
  minQty: number;
  isSufficient: boolean;
  note: string | null;
}

export default function SystemDetailPage() {
  const { id } = useParams<{ id: string }>();
  const navigate = useNavigate();
  const { message, modal } = App.useApp();
  const isMobile = useIsMobile();
  const canEdit = usePermission('systems.edit');
  const [spareForm] = Form.useForm();

  const [system, setSystem] = useState<SystemInfoDetailDto | null>(null);
  const [loading, setLoading] = useState(true);
  const [positionFilter, setPositionFilter] = useState<string | undefined>(undefined);

  const [assetCount, setAssetCount] = useState(0);
  const [accessoryCount, setAccessoryCount] = useState(0);
  const [maintenanceCount, setMaintenanceCount] = useState(0);
  const [licenseCount, setLicenseCount] = useState(0);
  const [spareParts, setSpareParts] = useState<SystemSparePartDto[]>([]);
  const [spareModalOpen, setSpareModalOpen] = useState(false);
  const [editingSpare, setEditingSpare] = useState<SystemSparePartDto | null>(null);
  const [spareType, setSpareType] = useState<'Component' | 'Consumable'>('Component');
  const [spareOptions, setSpareOptions] = useState<{ label: string; value: string; type: 'Component' | 'Consumable' }[]>([]);
  const [spareSaving, setSpareSaving] = useState(false);

  const load = useCallback(async () => {
    if (!id) return;
    setLoading(true);
    try {
      const res = await systemsService.get(id);
      setSystem(res.data.data as SystemInfoDetailDto);
    } catch {
      message.error('Không thể tải thông tin hệ thống');
    } finally {
      setLoading(false);
    }
  }, [id, message]);

  useEffect(() => { void load(); }, [load]);

  useEffect(() => {
    if (!id) return;
    let cancelled = false;
    void (async () => {
      try {
        const [aRes, accRes, mRes, licRes, spareRes] = await Promise.all([
          systemsService.listAssets(id, { page: 1, pageSize: 1 }),
          systemsService.listAccessories(id),
          apiClient.get('/maintenances', { params: { systemInfoId: id, page: 1, pageSize: 1 } }),
          licensesApi.forSystem(id),
          apiClient.get(`/systems/${id}/spare-parts`).catch(() => ({ data: { data: [] } })),
        ]);
        if (cancelled) return;
        setAssetCount(aRes.data.pagination?.totalItems ?? 0);
        setAccessoryCount((accRes.data?.data ?? []).length);
        setMaintenanceCount(mRes.data.pagination?.totalItems ?? 0);
        setLicenseCount((licRes.data?.data ?? []).length);
        setSpareParts((spareRes.data?.data ?? []) as SystemSparePartDto[]);
      } catch {
        /* non-critical */
      }
    })();
    return () => { cancelled = true; };
  }, [id]);

  const tableParams = useMemo(
    () => ({ refreshKey: `${id}|${positionFilter ?? ''}` }),
    [id, positionFilter],
  );

  const spareColumns = [
    {
      title: 'Loại', dataIndex: 'type', key: 'type', width: 120,
      render: (value: string) => (
        <Tag color={value === 'Component' ? 'blue' : 'cyan'}>
          {value === 'Component' ? 'Linh kiện' : 'Vật tư'}
        </Tag>
      ),
    },
    {
      title: 'Tên', dataIndex: 'name', key: 'name', width: 220,
      render: (_: unknown, record: SystemSparePartDto) => <EllipsisText value={record.name} />,
    },
    {
      title: 'Mã', dataIndex: 'code', key: 'code', width: 150,
      render: (_: unknown, record: SystemSparePartDto) => record.code
        ? <NowrapText value={record.code} code />
        : <Text type="secondary">—</Text>,
    },
    {
      title: 'Còn lại', dataIndex: 'remaining', key: 'remaining', width: 100, align: 'right' as const,
      render: (_: unknown, record: SystemSparePartDto) => <Text strong>{record.remaining}</Text>,
    },
    {
      title: 'Định mức', dataIndex: 'minQty', key: 'minQty', width: 100, align: 'right' as const,
      render: (_: unknown, record: SystemSparePartDto) => <Text>{record.minQty}</Text>,
    },
    {
      title: 'Trạng thái', dataIndex: 'isSufficient', key: 'isSufficient', width: 130,
      render: (_: unknown, record: SystemSparePartDto) => (
        <Tag color={record.isSufficient ? 'green' : 'red'}>
          {record.isSufficient ? 'Đủ' : 'Thiếu'}
        </Tag>
      ),
    },
    ...(canEdit ? [{
      title: 'Thao tác', key: 'actions', width: 150, fixed: 'right' as const,
      render: (_: unknown, record: SystemSparePartDto) => renderSpareActions(record),
    }] : []),
  ];

  const reloadSpareParts = async () => {
    if (!id) return;
    try {
      const res = await apiClient.get(`/systems/${id}/spare-parts`);
      setSpareParts((res.data?.data ?? []) as SystemSparePartDto[]);
    } catch {
      /* keep the current list */
    }
  };

  const loadSpareOptions = async () => {
    try {
      const [components, consumables] = await Promise.all([
        apiClient.get('/components', { params: { page: 1, pageSize: 500 } }),
        apiClient.get('/consumables', { params: { page: 1, pageSize: 500 } }),
      ]);
      setSpareOptions([
        ...((components.data?.data ?? []) as Array<{ id: string; name: string }>)
          .map((x) => ({ label: x.name, value: x.id, type: 'Component' as const })),
        ...((consumables.data?.data ?? []) as Array<{ id: string; name: string }>)
          .map((x) => ({ label: x.name, value: x.id, type: 'Consumable' as const })),
      ]);
    } catch {
      setSpareOptions([]);
    }
  };

  const openSpareCreate = () => {
    setEditingSpare(null);
    setSpareType('Component');
    spareForm.resetFields();
    spareForm.setFieldsValue({ type: 'Component' });
    void loadSpareOptions();
    setSpareModalOpen(true);
  };

  const openSpareEdit = (record: SystemSparePartDto) => {
    setEditingSpare(record);
    setSpareType(record.type);
    void loadSpareOptions();
    spareForm.setFieldsValue({ type: record.type, minQty: record.minQty, note: record.note ?? '' });
    setSpareModalOpen(true);
  };

  const saveSpare = async () => {
    const values = await spareForm.validateFields();
    setSpareSaving(true);
    try {
      if (editingSpare) {
        await apiClient.put(`/systems/${id}/spare-parts/${editingSpare.id}`, {
          minQty: values.minQty,
          note: values.note ?? null,
        });
        void message.success('Đã cập nhật định mức');
      } else {
        await apiClient.post(`/systems/${id}/spare-parts`, {
          componentId: values.type === 'Component' ? values.itemId : null,
          consumableId: values.type === 'Consumable' ? values.itemId : null,
          minQty: values.minQty,
          note: values.note ?? null,
        });
        void message.success('Đã thêm định mức');
      }
      setSpareModalOpen(false);
      await reloadSpareParts();
    } catch (err: unknown) {
      const e = err as { response?: { data?: { message?: string } } };
      void message.error(e?.response?.data?.message ?? 'Không thể lưu định mức');
    } finally {
      setSpareSaving(false);
    }
  };

  const confirmDeleteSpare = (record: SystemSparePartDto) => {
    modal.confirm({
      title: 'Xóa định mức vật tư dự phòng?',
      content: `${record.name} — định mức ${record.minQty}.`,
      okText: 'Xóa',
      cancelText: 'Hủy',
      okButtonProps: { danger: true },
      onOk: async () => {
        try {
          await apiClient.delete(`/systems/${id}/spare-parts/${record.id}`);
          void message.success('Đã xóa định mức');
          await reloadSpareParts();
        } catch {
          void message.error('Không thể xóa định mức');
          throw new Error('delete failed');
        }
      },
    });
  };

  const renderSpareActions = (record: SystemSparePartDto) => (
    <ListRowActions
      primary={{ key: 'edit', label: 'Sửa', icon: <EditOutlined />, onClick: () => openSpareEdit(record) }}
      menuItems={[{
        key: 'delete', label: 'Xóa', icon: <DeleteOutlined />, danger: true,
        onClick: () => confirmDeleteSpare(record),
      }]}
    />
  );

  if (loading) return <Spin style={{ display: 'block', margin: '80px auto' }} />;
  if (!system) return (
    <Empty description="Không tìm thấy hệ thống" style={{ marginTop: 80 }} />
  );

  const togglePositionFilter = (positionId: string) => {
    setPositionFilter(prev => (prev === positionId ? undefined : positionId));
  };

  const assetColumns: ProColumns<SystemAssetDto>[] = [
    {
      title: 'Tên tài sản',
      dataIndex: 'name',
      width: 240,
      ellipsis: true,
      render: (_, record) => (
        <Link to={`/assets/${record.id}`}>{record.name} <Text type="secondary">({record.assetTag})</Text></Link>
      ),
    },
    { title: 'Mã tài sản', dataIndex: 'assetTag', width: 140, render: (_, record) => <Text code>{record.assetTag}</Text> },
    {
      title: 'Vị trí trong hệ thống',
      dataIndex: ['systemPosition', 'name'],
      width: 220,
      render: (_, record) => record.systemPosition
        ? `${record.systemPosition.name}${record.systemPosition.code ? ` (${record.systemPosition.code})` : ''}`
        : '-',
    },
    {
      title: 'Vị trí lưu kho',
      dataIndex: ['location', 'name'],
      width: 160,
      render: (_, record) => record.location?.name ?? '-',
    },
    {
      title: 'User đang gán',
      key: 'assignedTo',
      width: 200,
      render: (_, record) => {
        if (!record.assignedTo?.name) return '-';
        const label = ASSIGNMENT_TARGET_LABELS[record.assignedTo.type] ?? record.assignedTo.type;
        return (
          <Space size={4} wrap>
            <Tag color="blue">{label}</Tag>
            <Text>{record.assignedTo.name}</Text>
          </Space>
        );
      },
    },
    {
      title: 'Phòng ban',
      dataIndex: ['department', 'name'],
      width: 140,
      render: (_, record) => record.department?.name ?? '-',
    },
    {
      title: 'Trạng thái',
      dataIndex: 'status',
      width: 130,
      render: (_, record) => {
        const s = record.status as AssetStatus;
        return <Tag color={ASSET_STATUS_COLORS[s]}>{ASSET_STATUS_LABELS[s] ?? record.status}</Tag>;
      },
    },
  ];

  const accessoryColumns: ProColumns<SystemAccessoryDto>[] = [
    {
      title: 'Tên phụ kiện',
      dataIndex: 'accessoryName',
      width: 240,
      ellipsis: true,
      render: (_, record) => (
        <Link to={`/accessories/${record.accessoryId}/view`}>
          {record.accessoryName}
          {record.accessoryItemNo ? <Text type="secondary"> ({record.accessoryItemNo})</Text> : null}
        </Link>
      ),
    },
    {
      title: 'Số lượng đã cấp',
      dataIndex: 'remainingCheckedOut',
      width: 130,
      render: (_, record) => (
        <Tag color="blue">{record.remainingCheckedOut}</Tag>
      ),
    },
    {
      title: 'Vị trí trong hệ thống',
      dataIndex: ['systemPosition', 'name'],
      width: 220,
      render: (_, record) => record.systemPosition
        ? `${record.systemPosition.name}${record.systemPosition.code ? ` (${record.systemPosition.code})` : ''}`
        : '-',
    },
    {
      title: 'Ngày cấp phát',
      dataIndex: 'checkedOutAt',
      width: 130,
      render: (_, record) => formatDate(record.checkedOutAt),
    },
    {
      title: 'Người thực hiện',
      dataIndex: 'createdByName',
      width: 180,
      ellipsis: true,
      render: (_, record) => record.createdByName ?? '-',
    },
    {
      title: 'Ghi chú',
      dataIndex: 'note',
      ellipsis: true,
      render: (_, record) => record.note ?? '-',
    },
  ];

  return (
    <div>
      {/* ─── Header ─── */}
      <Space style={{ marginBottom: 16 }} wrap>
        <Button icon={<ArrowLeftOutlined />} onClick={() => navigate('/admin/system-infos')}>Danh sách hệ thống</Button>
        <Title level={4} style={{ margin: 0 }}>
          <ClusterOutlined /> {system.code} — {system.name}
        </Title>
        {system.company
          ? <Tag color="blue">{system.company.name}</Tag>
          : <Tag>Chưa gán công ty</Tag>}
        {positionFilter && (
          <Tag color="orange" closable onClose={() => setPositionFilter(undefined)}>
            Lọc theo vị trí: {system.positions.find(p => p.id === positionFilter)?.name ?? positionFilter}
          </Tag>
        )}
      </Space>

      {/* ─── A. Thông tin chung ─── */}
      <Card title="Thông tin chung" size="small" style={{ marginBottom: 16 }}>
        <Descriptions column={{ xs: 1, sm: 2, md: 3 }} size="small" bordered>
          <Descriptions.Item label="Mã hệ thống"><Text code>{system.code}</Text></Descriptions.Item>
          <Descriptions.Item label="Tên hệ thống">{system.name}</Descriptions.Item>
          <Descriptions.Item label="Công ty">{system.company?.name ?? 'Chưa gán'}</Descriptions.Item>
          <Descriptions.Item label="Mô tả" span={{ xs: 1, sm: 2, md: 3 }}>{system.description || '-'}</Descriptions.Item>
        </Descriptions>

        <Divider titlePlacement="left" plain style={{ whiteSpace: 'normal' }}>Vị trí hệ thống — bấm vào 1 dòng để lọc các bảng bên dưới</Divider>
        <Table
          rowKey="id"
          size="small"
          dataSource={system.positions}
          pagination={false}
          scroll={{ x: 'max-content' }}
          rowClassName={(record) => (positionFilter === record.id ? 'ant-table-row-selected' : '')}
          onRow={(record) => ({ onClick: () => togglePositionFilter(record.id), style: { cursor: 'pointer' } })}
          locale={{ emptyText: <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} description="Hệ thống chưa có vị trí nào" /> }}
          columns={[
            { title: 'Mã vị trí', dataIndex: 'code', width: 160, render: (v: string) => <Text code>{v}</Text> },
            { title: 'Tên vị trí', dataIndex: 'name', width: 220 },
            { title: 'Mô tả', dataIndex: 'description', render: (v: string | null | undefined) => v || '-' },
          ]}
        />
      </Card>

      {/* ─── B. Lịch sử hệ thống ─── */}
      {/* ─── C. Tài sản / Phụ kiện / Bảo trì đang cấp phát ─── */}
      <Card size="small" style={{ overflow: 'hidden' }}>
        <Tabs
          className="system-detail-tabs"
          tabBarGutter={12}
          more={{ icon: <EllipsisOutlined /> }}
          items={[
            {
              key: 'assets',
              label: (
                <Space size={4}>
                  <LaptopOutlined /> Tài sản
                  <Tag color="blue">{assetCount}</Tag>
                </Space>
              ),
              children: isMobile ? (
                <ProList<SystemAssetDto>
                  rowKey="id"
                  ghost
                  cardProps={false}
                  grid={{ gutter: 12, xs: 1, sm: 1, md: 2, lg: 2, xl: 2, xxl: 2 }}
                  search={false}
                  params={tableParams}
                  request={async (params) => {
                    try {
                      const res = await systemsService.listAssets(id!, {
                        page: params.current ?? 1,
                        pageSize: params.pageSize ?? 10,
                        systemPositionId: positionFilter || undefined,
                      });
                      return {
                        data: (res.data?.data ?? []) as SystemAssetDto[],
                        total: res.data.pagination?.totalItems ?? 0,
                        success: true,
                      };
                    } catch {
                      return { data: [], success: false, total: 0 };
                    }
                  }}
                  pagination={{ pageSize: 10, showSizeChanger: false,
                    showTotal: (total, range) => `${range[0]}–${range[1]} / ${total} tài sản` }}
                  itemRender={(record) => (
                    <Card size="small" style={{ borderRadius: 10, marginBottom: 12 }}>
                      <Link to={`/assets/${record.id}`} style={{ fontWeight: 600 }}>{record.name}</Link>
                      <div style={{ marginTop: 6, display: 'flex', gap: 6, flexWrap: 'wrap' }}>
                        <NowrapText value={record.assetTag} code />
                        <Tag color={ASSET_STATUS_COLORS[record.status as AssetStatus]}>
                          {ASSET_STATUS_LABELS[record.status as AssetStatus] ?? record.status}
                        </Tag>
                      </div>
                      <div style={{ marginTop: 8, display: 'grid', gridTemplateColumns: '1fr 1fr', gap: '4px 12px', fontSize: 12 }}>
                        <span><Text type="secondary">Vị trí:</Text> <EllipsisText value={record.systemPosition?.name} /></span>
                        <span><Text type="secondary">Lưu kho:</Text> <EllipsisText value={record.location?.name} /></span>
                      </div>
                    </Card>
                  )}
                  locale={{ emptyText: <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} description="Hệ thống chưa có tài sản nào được cấp phát" /> }}
                />
              ) : (
                <ProTable<SystemAssetDto>
                  rowKey="id"
                  search={false}
                  options={{ density: true, reload: true }}
                  params={tableParams}
                  scroll={{ x: 'max-content' }}
                  pagination={{ pageSize: 10, showSizeChanger: true,
                    showTotal: (total, range) => `${range[0]}–${range[1]} / ${total} tài sản` }}
                  columns={assetColumns}
                  request={async (params) => {
                    try {
                      const res = await systemsService.listAssets(id!, {
                        page: params.current ?? 1,
                        pageSize: params.pageSize ?? 10,
                        systemPositionId: positionFilter || undefined,
                      });
                      return {
                        data: (res.data?.data ?? []) as SystemAssetDto[],
                        total: res.data.pagination?.totalItems ?? 0,
                        success: true,
                      };
                    } catch {
                      return { data: [], success: false, total: 0 };
                    }
                  }}
                  locale={{ emptyText: <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} description="Hệ thống chưa có tài sản nào được cấp phát" /> }}
                />
              ),
            },
            {
              key: 'accessories',
              label: (
                <Space size={4}>
                  <GiftOutlined /> Phụ kiện
                  <Tag color="blue">{accessoryCount}</Tag>
                </Space>
              ),
              children: isMobile ? (
                <ProList<SystemAccessoryDto>
                  rowKey="id"
                  ghost
                  cardProps={false}
                  grid={{ gutter: 12, xs: 1, sm: 1, md: 2, lg: 2, xl: 2, xxl: 2 }}
                  search={false}
                  params={tableParams}
                  request={async () => {
                    try {
                      const res = await systemsService.listAccessories(id!, {
                        systemPositionId: positionFilter || undefined,
                      });
                      const data = (res.data?.data ?? []) as SystemAccessoryDto[];
                      return { data, total: data.length, success: true };
                    } catch {
                      return { data: [], success: false, total: 0 };
                    }
                  }}
                  pagination={{ pageSize: 10, showSizeChanger: false,
                    showTotal: (total, range) => `${range[0]}–${range[1]} / ${total} phụ kiện` }}
                  itemRender={(record) => (
                    <Card size="small" style={{ borderRadius: 10, marginBottom: 12 }}>
                      <Link to={`/accessories/${record.accessoryId}/view`} style={{ fontWeight: 600 }}>
                        {record.accessoryName}
                      </Link>
                      <div style={{ marginTop: 6, display: 'flex', gap: 6, flexWrap: 'wrap' }}>
                        {record.accessoryItemNo && <NowrapText value={record.accessoryItemNo} code />}
                        <Tag color="blue">{record.remainingCheckedOut}</Tag>
                      </div>
                      <div style={{ marginTop: 8, fontSize: 12 }}>
                        <Text type="secondary">Vị trí:</Text> <EllipsisText value={record.systemPosition?.name} />
                      </div>
                    </Card>
                  )}
                  locale={{ emptyText: <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} description="Hệ thống chưa có phụ kiện nào được cấp phát" /> }}
                />
              ) : (
                <ProTable<SystemAccessoryDto>
                  rowKey="id"
                  search={false}
                  options={{ density: true, reload: true }}
                  params={tableParams}
                  scroll={{ x: 'max-content' }}
                  pagination={{ pageSize: 10, showSizeChanger: true,
                    showTotal: (total, range) => `${range[0]}–${range[1]} / ${total} phụ kiện` }}
                  columns={accessoryColumns}
                  request={async () => {
                    try {
                      const res = await systemsService.listAccessories(id!, {
                        systemPositionId: positionFilter || undefined,
                      });
                      const data = (res.data?.data ?? []) as SystemAccessoryDto[];
                      return { data, total: data.length, success: true };
                    } catch {
                      return { data: [], success: false, total: 0 };
                    }
                  }}
                  locale={{ emptyText: <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} description="Hệ thống chưa có phụ kiện nào được cấp phát" /> }}
                />
              ),
            },
            {
              key: 'spare-parts',
              label: (
                <Space size={4}>
                  <ToolOutlined /> Vật tư dự phòng
                  <Tag color="cyan">{spareParts.length}</Tag>
                </Space>
              ),
              children: (
                <>
                  {canEdit && (
                    <div style={{ display: 'flex', justifyContent: 'flex-end', marginBottom: 12 }}>
                      <Button type="primary" icon={<PlusOutlined />} onClick={openSpareCreate}>
                        Thêm định mức
                      </Button>
                    </div>
                  )}
                  {isMobile ? (
                spareParts.length === 0 ? (
                  <Empty
                    image={Empty.PRESENTED_IMAGE_SIMPLE}
                    description="Hệ thống chưa có định mức vật tư dự phòng. Bấm &quot;Thêm định mức&quot; để tạo."
                  />
                ) : (
                  <Space orientation="vertical" size={12} style={{ width: '100%' }}>
                    {spareParts.map((part) => (
                      <Card key={part.id} size="small" style={{ borderRadius: 10 }}>
                        <Space size={[4, 6]} wrap style={{ marginBottom: 8 }}>
                          <Tag color={part.type === 'Component' ? 'blue' : 'cyan'}>
                            {part.type === 'Component' ? 'Linh kiện' : 'Vật tư'}
                          </Tag>
                          <Tag color={part.isSufficient ? 'green' : 'red'}>
                            {part.isSufficient ? 'Đủ' : 'Thiếu'}
                          </Tag>
                        </Space>
                        <div style={{ fontWeight: 600, marginBottom: 4 }}>{part.name}</div>
                        <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: '4px 12px', fontSize: 12 }}>
                          <span><Text type="secondary">Mã:</Text> {part.code ? <NowrapText value={part.code} code /> : <Text type="secondary">—</Text>}</span>
                          <span><Text type="secondary">Còn lại:</Text> <Text strong>{part.remaining}</Text></span>
                          <span><Text type="secondary">Định mức:</Text> <Text>{part.minQty}</Text></span>
                        </div>
                        {canEdit && <div style={{ display: 'flex', justifyContent: 'flex-end', marginTop: 10 }}>{renderSpareActions(part)}</div>}
                      </Card>
                    ))}
                  </Space>
                )
              ) : (
                <Table<SystemSparePartDto>
                  rowKey="id"
                  size="small"
                  columns={spareColumns}
                  dataSource={spareParts}
                  pagination={false}
                  scroll={{ x: 'max-content' }}
                  locale={{
                    emptyText: (
                      <Empty
                        image={Empty.PRESENTED_IMAGE_SIMPLE}
                        description="Hệ thống chưa có định mức vật tư dự phòng. Bấm &quot;Thêm định mức&quot; để tạo."
                      />
                    ),
                  }}
                />
                  )}
                </>
              ),
            },
            {
              key: 'history',
              label: (
                <Space size={4}>
                  <HistoryOutlined /> Lịch sử hệ thống
                </Space>
              ),
              children: (
                <div>
                  <div style={{ textAlign: 'right', marginBottom: 8 }}>
                    <Link to="/system-history"><HistoryOutlined /> Xem đầy đủ lịch sử</Link>
                  </div>
                  <ActionLogTable
                    targetColumnTitle="Vị trí lắp đặt"
                    emptyText="Chưa có lịch sử cấp phát nào cho hệ thống này."
                    params={tableParams}
                    extraColumns={[
                      {
                        title: 'Tài sản', dataIndex: 'itemName', key: 'itemName', width: 220, ellipsis: true,
                        render: (_, record) => record.itemName || '-',
                      },
                    ]}
                    request={async (params) => {
                      try {
                        const res = await apiClient.get('/action-logs/by-system', {
                          params: {
                            systemInfoId: id,
                            page: params.current ?? 1,
                            pageSize: params.pageSize ?? 10,
                          },
                        });
                        return { data: (res.data?.data ?? []) as ActionLogRow[], success: true, total: res.data?.total ?? 0 };
                      } catch {
                        return { data: [], success: false, total: 0 };
                      }
                    }}
                  />
                </div>
              ),
            },
            {
              key: 'maintenance',
              label: (
                <Space size={4}>
                  <ExperimentOutlined /> Bảo trì
                  <Tag color="blue">{maintenanceCount}</Tag>
                </Space>
              ),
              children: (
                <MaintenanceTable systemInfoId={id} />
              ),
            },
            {
              key: 'license',
              label: (
                <Space size={4}>
                  <KeyOutlined /> Bản quyền
                  <Tag color="purple">{licenseCount}</Tag>
                </Space>
              ),
              children: (
                <LicenseUsageTable scope={{ type: 'system', id: id! }} />
              ),
            },
            {
              // [MC-6] Lịch sử bảo dưỡng — các đợt Campaign theo hệ thống (đặt sau License, đúng thiết kế duyệt).
              key: 'maintenance-campaigns',
              label: (
                <Space size={4}>
                  <CarOutlined /> Lịch sử bảo dưỡng
                </Space>
              ),
              children: (
                <CampaignHistoryTable systemInfoId={id!} />
              ),
            },
          ]}
        />
      </Card>

      <Modal
        open={spareModalOpen}
        title={editingSpare ? 'Sửa định mức vật tư dự phòng' : 'Thêm định mức vật tư dự phòng'}
        onOk={() => void saveSpare()}
        confirmLoading={spareSaving}
        onCancel={() => setSpareModalOpen(false)}
        okText="Lưu"
        cancelText="Hủy"
        destroyOnHidden
        forceRender
        width={isMobile ? '95%' : 520}
      >
        <Form form={spareForm} layout="vertical" initialValues={{ type: 'Component' }}>
          <Form.Item label="Loại" name="type" rules={[{ required: true }]}>
            <Segmented
              disabled={!!editingSpare}
              options={[
                { label: 'Linh kiện', value: 'Component' },
                { label: 'Vật tư', value: 'Consumable' },
              ]}
              onChange={(value) => {
                setSpareType(value as 'Component' | 'Consumable');
                spareForm.setFieldValue('itemId', undefined);
              }}
            />
          </Form.Item>
          <Form.Item
            label={spareType === 'Component' ? 'Linh kiện' : 'Vật tư'}
            name="itemId"
            rules={[{ required: true, message: 'Chọn linh kiện/vật tư' }]}
          >
            <Select
              showSearch
              disabled={!!editingSpare}
              placeholder="Tìm và chọn..."
              optionFilterProp="label"
              options={spareOptions.filter((o) =>
                o.type === spareType
                && (!spareParts.some((p) => p.itemId === o.value)
                  || (editingSpare != null && editingSpare.itemId === o.value)))}
            />
          </Form.Item>
          <Form.Item
            label="Định mức"
            name="minQty"
            rules={[
              { required: true, message: 'Nhập định mức' },
              {
                type: 'number',
                min: editingSpare ? 0 : 1,
                message: editingSpare ? 'Định mức phải >= 0' : 'Định mức phải >= 1',
              },
            ]}
          >
            <InputNumber min={editingSpare ? 0 : 1} precision={0} style={{ width: '100%' }} placeholder="Số lượng tối thiểu" />
          </Form.Item>
          <Form.Item label="Ghi chú" name="note">
            <Input.TextArea rows={3} maxLength={1000} showCount />
          </Form.Item>
        </Form>
      </Modal>
    </div>
  );
}
