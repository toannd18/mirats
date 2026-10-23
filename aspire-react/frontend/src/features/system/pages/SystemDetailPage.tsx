import { useCallback, useEffect, useMemo, useState } from 'react';
import { Link, useNavigate, useParams, useSearchParams } from 'react-router-dom';
import {
  App, Button, Card, Descriptions, Empty, Form, Input, InputNumber, Result, Segmented, Select, Space, Spin, Table, Tabs, Tag, Tooltip, Typography,
} from 'antd';
import {
  ArrowLeftOutlined, CarOutlined, ClusterOutlined, EllipsisOutlined, ExperimentOutlined, GiftOutlined, HistoryOutlined, KeyOutlined, LaptopOutlined, PrinterOutlined, RollbackOutlined, ToolOutlined,
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
import { formatDate, formatDateTime } from '../../../utils/format';
import { useIsMobile } from '../../../hooks/useIsMobile';
import { EllipsisText, NowrapText } from '../../../shared/components/list/ListText';
import ListRowActions from '../../../shared/components/list/ListRowActions';
import { usePermission } from '../../../hooks/usePermission';
import ModalFormShell from '../../../shared/components/ModalFormShell';

const { Title, Text } = Typography;

const ASSIGNMENT_TARGET_LABELS: Record<string, string> = {
  User: 'Người dùng',
  Department: 'Phòng ban',
  SystemPosition: 'Hệ thống',
};

interface SystemSparePartDto {
  itemId: string;
  type: 'Component' | 'Consumable';
  name: string;
  code: string | null;
  issuedToSystem: number;
  remaining: number;
  isSerial: boolean;
  lastIssuedAt: string | null;
}

interface SpareItemOption {
  label: string;
  value: string;
  type: 'Component' | 'Consumable';
  name: string;
  remaining: number;
  isSerial: boolean;
}

export default function SystemDetailPage() {
  const { id } = useParams<{ id: string }>();
  const navigate = useNavigate();
  const { message } = App.useApp();
  const isMobile = useIsMobile();
  const canCheckoutComponent = usePermission('components.checkout');
  const canCheckoutConsumable = usePermission('consumables.checkout');
  const [movementForm] = Form.useForm();
  const movementType = Form.useWatch('type', movementForm) as 'Component' | 'Consumable' | undefined;
  const movementItemId = Form.useWatch('itemId', movementForm) as string | undefined;

  const [system, setSystem] = useState<SystemInfoDetailDto | null>(null);
  const [loading, setLoading] = useState(true);
  const [loadError, setLoadError] = useState<string | null>(null);
  const [searchParams] = useSearchParams();
  const [positionFilter, setPositionFilter] = useState<string | undefined>(
    () => searchParams.get('position') ?? undefined,
  );

  const [assetCount, setAssetCount] = useState(0);
  const [accessoryCount, setAccessoryCount] = useState(0);
  const [maintenanceCount, setMaintenanceCount] = useState(0);
  const [licenseCount, setLicenseCount] = useState(0);
  const [spareParts, setSpareParts] = useState<SystemSparePartDto[]>([]);
  const [movementModalOpen, setMovementModalOpen] = useState(false);
  const [movementRecord, setMovementRecord] = useState<SystemSparePartDto | null>(null);
  const [movementDirection, setMovementDirection] = useState<'issue' | 'return'>('issue');
  const [movementSaving, setMovementSaving] = useState(false);
  const [movementOptions, setMovementOptions] = useState<SpareItemOption[]>([]);
  const [movementOptionsLoading, setMovementOptionsLoading] = useState(false);

  const load = useCallback(async () => {
    if (!id) return;
    setLoading(true);
    setLoadError(null);
    try {
      const res = await systemsService.get(id);
      setSystem(res.data.data as SystemInfoDetailDto);
    } catch (err: unknown) {
      const e = err as { response?: { status?: number; data?: { message?: string } } };
      setSystem(null);
      setLoadError(e?.response?.status === 404
        ? 'Không tìm thấy hệ thống hoặc hệ thống nằm ngoài phạm vi công ty của bạn.'
        : (e?.response?.data?.message || 'Không thể tải thông tin hệ thống.'));
    } finally {
      setLoading(false);
    }
  }, [id]);

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
      title: 'Đã cấp ròng', dataIndex: 'issuedToSystem', key: 'issuedToSystem', width: 130, align: 'right' as const,
      render: (_: unknown, record: SystemSparePartDto) => <Text strong>{record.issuedToSystem}</Text>,
    },
    {
      title: 'Tồn kho hiện tại', dataIndex: 'remaining', key: 'remaining', width: 140, align: 'right' as const,
      render: (_: unknown, record: SystemSparePartDto) => <Text>{record.remaining}</Text>,
    },
    {
      title: 'Lần cấp gần nhất', dataIndex: 'lastIssuedAt', key: 'lastIssuedAt', width: 170,
      render: (_: unknown, record: SystemSparePartDto) => record.lastIssuedAt
        ? formatDateTime(record.lastIssuedAt)
        : <Text type="secondary">—</Text>,
    },
    ...(canCheckoutComponent || canCheckoutConsumable ? [{
      title: 'Thao tác', key: 'actions', width: 160, fixed: 'right' as const,
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
    setMovementOptionsLoading(true);
    try {
      const [components, consumables] = await Promise.all([
        apiClient.get('/components', { params: { page: 1, pageSize: 500 } }),
        apiClient.get('/consumables', { params: { page: 1, pageSize: 500 } }),
      ]);
      const componentOptions = ((components.data?.data ?? []) as Array<{
        id: string; name: string; remaining: number; trackingType: string;
      }>)
        .filter((x) => x.trackingType !== 'Serial' && x.remaining > 0)
        .map((x) => ({
          label: `${x.name} — tồn ${x.remaining}`, value: x.id, type: 'Component' as const,
          name: x.name, remaining: x.remaining, isSerial: false,
        }));
      const consumableOptions = ((consumables.data?.data ?? []) as Array<{
        id: string; name: string; remaining: number;
      }>)
        .filter((x) => x.remaining > 0)
        .map((x) => ({
          label: `${x.name} — tồn ${x.remaining}`, value: x.id, type: 'Consumable' as const,
          name: x.name, remaining: x.remaining, isSerial: false,
        }));
      setMovementOptions([...componentOptions, ...consumableOptions]);
    } catch {
      setMovementOptions([]);
    } finally {
      setMovementOptionsLoading(false);
    }
  };

  const openMovement = (record: SystemSparePartDto | null, direction: 'issue' | 'return') => {
    if (record?.isSerial) return;
    setMovementRecord(record);
    setMovementDirection(direction);
    movementForm.resetFields();
    if (record) {
      movementForm.setFieldsValue({
        type: record.type, itemId: record.itemId, quantity: 1, note: '',
      });
    } else {
      movementForm.setFieldsValue({ type: 'Component', quantity: 1 });
      void loadSpareOptions();
    }
    setMovementModalOpen(true);
  };

  const saveMovement = async () => {
    const values = await movementForm.validateFields();
    setMovementSaving(true);
    try {
      await apiClient.post(`/systems/${id}/spare-parts/${movementDirection}`, {
        componentId: values.type === 'Component' ? values.itemId : null,
        consumableId: values.type === 'Consumable' ? values.itemId : null,
        quantity: values.quantity,
        note: values.note ?? null,
      });
      void message.success(movementDirection === 'issue' ? 'Đã cấp vật tư cho hệ thống' : 'Đã nhận lại vật tư');
      setMovementModalOpen(false);
      await reloadSpareParts();
    } catch (err: unknown) {
      const e = err as { response?: { data?: { message?: string } } };
      void message.error(e?.response?.data?.message
        ?? (movementDirection === 'issue' ? 'Không thể cấp vật tư' : 'Không thể nhận lại vật tư'));
    } finally {
      setMovementSaving(false);
    }
  };

  const canMoveSpare = (record: SystemSparePartDto) => !record.isSerial
    && (record.type === 'Component' ? canCheckoutComponent : canCheckoutConsumable);

  const selectedMovementOption = movementOptions.find(
    (o) => o.value === movementItemId && o.type === movementType,
  );
  const movementMax = movementDirection === 'issue'
    ? (movementRecord?.remaining ?? selectedMovementOption?.remaining ?? 0)
    : (movementRecord?.issuedToSystem ?? 0);
  const selectedMovementName = movementRecord?.name ?? selectedMovementOption?.name ?? '';
  const selectedMovementType = movementRecord?.type ?? selectedMovementOption?.type ?? movementType;
  const selectedMovementRemaining = movementRecord?.remaining ?? selectedMovementOption?.remaining ?? 0;
  const selectedMovementIssued = movementRecord?.issuedToSystem
    ?? spareParts.find((p) => p.itemId === movementItemId)?.issuedToSystem ?? 0;

  const renderSpareActions = (record: SystemSparePartDto) => {
    if (!canMoveSpare(record)) return null;
    return (
      <ListRowActions
        primary={{
          key: 'issue', label: 'Cấp thêm', icon: <GiftOutlined />,
          disabled: record.remaining <= 0,
          onClick: () => openMovement(record, 'issue'),
        }}
        menuItems={[{
          key: 'return', label: 'Nhận lại', icon: <RollbackOutlined />,
          disabled: record.issuedToSystem <= 0,
          onClick: () => openMovement(record, 'return'),
        }]}
      />
    );
  };

  if (loading) return <Spin style={{ display: 'block', margin: '80px auto' }} />;
  if (loadError || !system) return (
    <Result
      status="404"
      title="Không tìm thấy hệ thống"
      subTitle={loadError ?? 'Hệ thống không tồn tại hoặc đã bị xóa.'}
      extra={[
        <Button key="back" onClick={() => navigate('/systems')}>Danh sách hệ thống</Button>,
        <Button key="home" type="primary" onClick={() => navigate('/dashboard')}>Về tổng quan</Button>,
      ]}
    />
  );

  const togglePositionFilter = (positionId: string) => {
    setPositionFilter(prev => (prev === positionId ? undefined : positionId));
  };

  const assetColumns: ProColumns<SystemAssetDto>[] = [
    {
      title: 'Tên tài sản',
      dataIndex: 'name',
      width: 190,
      ellipsis: true,
      render: (_, record) => (
        <Tooltip title={record.name} placement="topLeft">
          <Link
            to={`/assets/${record.id}`}
            style={{ display: 'block', overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}
          >
            {record.name}
          </Link>
        </Tooltip>
      ),
    },
    { title: 'Mã tài sản', dataIndex: 'assetTag', width: 130, render: (_, record) => <Text code>{record.assetTag}</Text> },
    {
      title: 'Vị trí trong hệ thống',
      dataIndex: ['systemPosition', 'name'],
      width: 160,
      render: (_, record) => <EllipsisText value={record.systemPosition?.name} />,
    },
    {
      title: 'Vị trí lưu kho',
      dataIndex: ['location', 'name'],
      width: 120,
      render: (_, record) => <EllipsisText value={record.location?.name} />,
    },
    {
      title: 'Đang giữ',
      key: 'assignedTo',
      width: 170,
      render: (_, record) => {
        if (!record.assignedTo?.name) return '-';
        const label = ASSIGNMENT_TARGET_LABELS[record.assignedTo.type] ?? record.assignedTo.type;
        return (
          <Space size={4} wrap={false}>
            <Tag color="blue">{label}</Tag>
            <span style={{ minWidth: 0, flex: 1 }}>
              <EllipsisText value={record.assignedTo.name} />
            </span>
          </Space>
        );
      },
    },
    {
      title: 'Phòng ban',
      dataIndex: ['department', 'name'],
      width: 110,
      render: (_, record) => <EllipsisText value={record.department?.name} />,
    },
    {
      title: 'Trạng thái',
      dataIndex: 'status',
      width: 110,
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

        <div style={{ margin: '8px 0 12px' }}>
          <Text strong style={{ fontSize: 15 }}>Vị trí</Text>
          <div>
            <Text type="secondary" style={{ fontSize: 12 }}>
              Bấm vào một dòng để lọc các bảng bên dưới.
            </Text>
          </div>
        </div>
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
            {
              title: 'Thao tác', key: 'position-actions', width: 120,
              render: (_: unknown, record: { id: string }) => (
                <Button
                  size="small"
                  icon={<PrinterOutlined />}
                  onClick={(e) => {
                    e.stopPropagation();
                    navigate(`/assets/labels/print?systemId=${id}&positionId=${record.id}`);
                  }}
                >
                  In tem
                </Button>
              ),
            },
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
                  <ToolOutlined /> Vật tư đã cấp
                  <Tag color="cyan">{spareParts.length}</Tag>
                </Space>
              ),
              children: (
                <>
                  {(canCheckoutComponent || canCheckoutConsumable) && (
                    <div style={{ display: 'flex', justifyContent: 'flex-end', marginBottom: 12 }}>
                      <Button type="primary" icon={<GiftOutlined />} onClick={() => openMovement(null, 'issue')}>
                        Cấp vật tư
                      </Button>
                    </div>
                  )}
                  {isMobile ? (
                    spareParts.length === 0 ? (
                      <Empty
                        image={Empty.PRESENTED_IMAGE_SIMPLE}
                        description="Hệ thống chưa nhận vật tư nào. Bấm &quot;Cấp vật tư&quot; để bắt đầu."
                      />
                    ) : (
                      <Space orientation="vertical" size={12} style={{ width: '100%' }}>
                        {spareParts.map((part) => (
                          <Card key={part.itemId} size="small" style={{ borderRadius: 10 }}>
                            <Space size={[4, 6]} wrap style={{ marginBottom: 8 }}>
                              <Tag color={part.type === 'Component' ? 'blue' : 'cyan'}>
                                {part.type === 'Component' ? 'Linh kiện' : 'Vật tư'}
                              </Tag>
                            </Space>
                            <div style={{ fontWeight: 600, marginBottom: 4 }}>{part.name}</div>
                            <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: '4px 12px', fontSize: 12 }}>
                              <span><Text type="secondary">Mã:</Text> {part.code ? <NowrapText value={part.code} code /> : <Text type="secondary">—</Text>}</span>
                              <span><Text type="secondary">Đã cấp ròng:</Text> <Text strong>{part.issuedToSystem}</Text></span>
                              <span><Text type="secondary">Tồn kho:</Text> <Text>{part.remaining}</Text></span>
                              <span><Text type="secondary">Lần cấp gần nhất:</Text> <Text>{part.lastIssuedAt ? formatDateTime(part.lastIssuedAt) : '—'}</Text></span>
                            </div>
                            <div style={{ display: 'flex', justifyContent: 'flex-end', marginTop: 10 }}>
                              {renderSpareActions(part)}
                            </div>
                          </Card>
                        ))}
                      </Space>
                    )
                  ) : (
                    <Table<SystemSparePartDto>
                      rowKey="itemId"
                      size="small"
                      columns={spareColumns}
                      dataSource={spareParts}
                      pagination={false}
                      scroll={{ x: 'max-content' }}
                      locale={{
                        emptyText: (
                          <Empty
                            image={Empty.PRESENTED_IMAGE_SIMPLE}
                            description="Hệ thống chưa nhận vật tư nào. Bấm &quot;Cấp vật tư&quot; để bắt đầu."
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
                        title: 'Tài sản', dataIndex: 'itemName', key: 'itemName', width: 180,
                        render: (_, record) => <EllipsisText value={record.itemName} />,
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

      <ModalFormShell
        open={movementModalOpen}
        title={movementDirection === 'issue' ? 'Cấp vật tư' : 'Nhận lại vật tư'}
        onOk={() => void saveMovement()}
        confirmLoading={movementSaving}
        onCancel={() => setMovementModalOpen(false)}
        okText={movementDirection === 'issue' ? 'Cấp' : 'Nhận lại'}
        size="sm"
      >
        <Form form={movementForm} layout="vertical">
          {movementDirection === 'issue' && (
            <>
              <Form.Item label="Loại" name="type" rules={[{ required: true }]}>
                <Segmented
                  disabled={!!movementRecord}
                  options={[
                    { label: 'Linh kiện', value: 'Component' },
                    { label: 'Vật tư', value: 'Consumable' },
                  ]}
                  onChange={() => movementForm.setFieldValue('itemId', undefined)}
                />
              </Form.Item>
              <Form.Item
                label="Mục"
                name="itemId"
                rules={[{ required: true, message: 'Chọn linh kiện/vật tư' }]}
              >
                <Select
                  showSearch
                  loading={movementOptionsLoading}
                  disabled={!!movementRecord}
                  placeholder="Tìm và chọn mục còn tồn kho..."
                  optionFilterProp="label"
                  options={movementOptions.filter((o) => o.type === movementType)}
                />
              </Form.Item>
            </>
          )}

          {(movementRecord || selectedMovementOption) && (
            <Descriptions column={1} size="small" bordered style={{ marginBottom: 16 }}>
              <Descriptions.Item label="Mục">{selectedMovementName}</Descriptions.Item>
              <Descriptions.Item label="Loại">
                {selectedMovementType === 'Component' ? 'Linh kiện' : 'Vật tư'}
              </Descriptions.Item>
              <Descriptions.Item label="Tồn kho hiện tại">
                <Text strong>{selectedMovementRemaining}</Text>
              </Descriptions.Item>
              <Descriptions.Item label="Đã cấp cho hệ thống">
                <Text strong>{selectedMovementIssued}</Text>
              </Descriptions.Item>
            </Descriptions>
          )}

          <Form.Item
            label="Số lượng"
            name="quantity"
            rules={[
              { required: true, message: 'Nhập số lượng' },
              {
                validator: (_, value: number | null | undefined) => {
                  if (value === null || value === undefined) return Promise.resolve();
                  if (value < 1) return Promise.reject(new Error('Số lượng phải >= 1'));
                  if (value > movementMax) {
                    return Promise.reject(new Error(
                      movementMax > 0 ? `Tối đa ${movementMax}` : 'Không có số lượng khả dụng',
                    ));
                  }
                  return Promise.resolve();
                },
              },
            ]}
          >
            <InputNumber min={1} precision={0} placeholder={`Tối đa ${movementMax}`} style={{ width: '100%' }} />
          </Form.Item>
          <Form.Item label="Ghi chú" name="note">
            <Input.TextArea rows={3} maxLength={1000} showCount />
          </Form.Item>
        </Form>
      </ModalFormShell>
    </div>
  );
}
