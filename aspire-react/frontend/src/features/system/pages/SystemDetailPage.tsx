import { useCallback, useEffect, useMemo, useState } from 'react';
import { Link, useNavigate, useParams } from 'react-router-dom';
import {
  App, Button, Card, Descriptions, Divider, Empty, Space, Spin, Table, Tabs, Tag, Typography,
} from 'antd';
import {
  ArrowLeftOutlined, CarOutlined, ClusterOutlined, ExperimentOutlined, GiftOutlined, HistoryOutlined, KeyOutlined, LaptopOutlined,
} from '@ant-design/icons';
import { ProTable } from '@ant-design/pro-components';
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

const { Title, Text } = Typography;

const ASSIGNMENT_TARGET_LABELS: Record<string, string> = {
  User: 'Người dùng',
  Department: 'Phòng ban',
  SystemPosition: 'Hệ thống',
};

export default function SystemDetailPage() {
  const { id } = useParams<{ id: string }>();
  const navigate = useNavigate();
  const { message } = App.useApp();

  const [system, setSystem] = useState<SystemInfoDetailDto | null>(null);
  const [loading, setLoading] = useState(true);
  const [positionFilter, setPositionFilter] = useState<string | undefined>(undefined);

  const [assetCount, setAssetCount] = useState(0);
  const [accessoryCount, setAccessoryCount] = useState(0);
  const [maintenanceCount, setMaintenanceCount] = useState(0);
  const [licenseCount, setLicenseCount] = useState(0);

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
        const [aRes, accRes, mRes, licRes] = await Promise.all([
          systemsService.listAssets(id, { page: 1, pageSize: 1 }),
          systemsService.listAccessories(id),
          apiClient.get('/maintenances', { params: { systemInfoId: id, page: 1, pageSize: 1 } }),
          licensesApi.forSystem(id),
        ]);
        if (cancelled) return;
        setAssetCount(aRes.data.pagination?.totalItems ?? 0);
        setAccessoryCount((accRes.data?.data ?? []).length);
        setMaintenanceCount(mRes.data.pagination?.totalItems ?? 0);
        setLicenseCount((licRes.data?.data ?? []).length);
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
    { title: 'Asset Tag', dataIndex: 'assetTag', width: 140, render: (_, record) => <Text code>{record.assetTag}</Text> },
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

        <Divider titlePlacement="left" plain>Vị trí (SystemPosition) — bấm vào 1 dòng để lọc các bảng bên dưới</Divider>
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
      <Card
        title="Lịch sử hệ thống"
        size="small"
        style={{ marginBottom: 16 }}
        extra={<Link to="/system-history"><HistoryOutlined /> Xem đầy đủ lịch sử</Link>}
      >
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
      </Card>

      {/* ─── C. Tài sản / Phụ kiện / Bảo trì đang cấp phát ─── */}
      <Card size="small">
        <Tabs
          items={[
            {
              key: 'assets',
              label: (
                <Space size={4}>
                  <LaptopOutlined /> Tài sản
                  <Tag color="blue">{assetCount}</Tag>
                </Space>
              ),
              children: (
                <ProTable<SystemAssetDto>
                  rowKey="id"
                  search={false}
                  options={{ density: true, reload: true }}
                  params={tableParams}
                  scroll={{ x: 'max-content' }}
                  pagination={{ pageSize: 10, showSizeChanger: true }}
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
              children: (
                <ProTable<SystemAccessoryDto>
                  rowKey="id"
                  search={false}
                  options={{ density: true, reload: true }}
                  params={tableParams}
                  scroll={{ x: 'max-content' }}
                  pagination={{ pageSize: 10, showSizeChanger: true }}
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
                  <KeyOutlined /> License
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
    </div>
  );
}
