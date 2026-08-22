import { useEffect, useState, useCallback, useRef, useMemo } from 'react';
import { useParams, useNavigate } from 'react-router-dom';
import { Descriptions, Tag, Spin, Button, Space, Card, message, Steps, Alert, Empty, Typography } from 'antd';
import {
  ArrowLeftOutlined, EditOutlined, AuditOutlined, InboxOutlined,
  RollbackOutlined, SendOutlined, UserOutlined, TeamOutlined, ClusterOutlined,
} from '@ant-design/icons';
import type { ActionType } from '@ant-design/pro-components';
import apiClient from '../../../services/api-client';
import ActionLogTable, { type ActionLogRow } from '../../../shared/components/ActionLogTable';
import AssetMaintenanceSection from '../components/AssetMaintenanceSection';
import AssetAllocationModal from '../components/AssetAllocationModal';
import { usePermission } from '../../../hooks/usePermission';
import LicenseUsageTable from '../../../shared/components/LicenseUsageTable';
import AssetRecallModal from '../components/AssetRecallModal';
import AssetArchiveModal from '../components/AssetArchiveModal';
import {
  ASSET_STATUS_LABELS, ASSET_STATUS_COLORS,
  getAssetActions, normalizeAssetStatus,
  type AssetDetailDto, type AssetStatus,
} from '../types/asset';
import { formatDate } from '../../../utils/format';

const { Text } = Typography;

const TARGET_TYPE_LABELS: Record<string, string> = {
  user: 'Người dùng',
  department: 'Phòng ban',
  systemPosition: 'Hệ thống',
};

const LIFECYCLE_STEPS: Array<{ title: string; description: AssetStatus }> = [
  { title: 'Chờ cấp phát', description: 'Pending' },
  { title: 'Đang sử dụng', description: 'Deployed' },
  { title: 'Đã lưu trữ', description: 'Archived' },
];

function lifecycleStepIndex(status: AssetStatus): number {
  return status === 'Pending' ? 0 : status === 'Deployed' ? 1 : 2;
}

const AssetDetailPage: React.FC = () => {
  const { id } = useParams<{ id: string }>();
  const navigate = useNavigate();
  const [asset, setAsset] = useState<AssetDetailDto | null>(null);
  const [loading, setLoading] = useState(true);
  const [allocOpen, setAllocOpen] = useState(false);
  const [recallOpen, setRecallOpen] = useState(false);
  const [archiveOpen, setArchiveOpen] = useState(false);
  const [refreshKey, setRefreshKey] = useState(0);
  const actionLogRef = useRef<ActionType>(null);
  // ST6b — permission gating (mirrors backend [Authorize(Policy=...)]).
  const canEdit = usePermission('assets.edit');
  const canCheckout = usePermission('assets.checkout');
  const canCheckin = usePermission('assets.checkin');
  const canAudit = usePermission('assets.audit');
  // Stable identity → ProTable only reloads when refreshKey actually changes.
  const actionLogParams = useMemo(() => ({ refreshKey }), [refreshKey]);

  const loadAsset = useCallback(() => {
    if (!id) return;
    setLoading(true);
    apiClient.get(`/assets/${id}`)
      .then(r => setAsset(r.data.data))
      .catch(() => message.error('Không thể tải thông tin tài sản'))
      .finally(() => setLoading(false));
  }, [id]);

  useEffect(() => { loadAsset(); }, [loadAsset]);

  const handleLifecycleSuccess = () => { setRefreshKey(k => k + 1); loadAsset(); };

  // Modal submit handlers: close the modal first, then refresh asset data + action log table.
  const handleAllocSuccess = () => { setAllocOpen(false); handleLifecycleSuccess(); };
  const handleRecallSuccess = () => { setRecallOpen(false); handleLifecycleSuccess(); };
  const handleArchiveSuccess = () => { setArchiveOpen(false); handleLifecycleSuccess(); };

  const handleAudit = async () => {
    try { await apiClient.post(`/api/v1/assets/${id}/audit`, {}); message.success('Đã kiểm kê tài sản'); handleLifecycleSuccess(); }
    catch { message.error('Kiểm kê thất bại'); }
  };

  if (loading) return <Spin size="large" />;
  if (!asset) return <div style={{ textAlign: 'center', padding: 80 }}>Không tìm thấy tài sản.</div>;

  const status = normalizeAssetStatus(asset.status);
  const actions = getAssetActions(asset);
  const has = (a: string) => actions.includes(a as never);
  const stepIndex = lifecycleStepIndex(status);
  const assignedName = asset.assignedTo?.name
    || [asset.assignedTo?.firstName, asset.assignedTo?.lastName].filter(Boolean).join(' ')
    || asset.assignedTo?.username
    || '-';
  const targetLabel = asset.assignedTo?.type ? TARGET_TYPE_LABELS[asset.assignedTo.type] ?? asset.assignedTo.type : null;

  return (
    <div>
      <Space style={{ marginBottom: 16 }} wrap>
        <Button icon={<ArrowLeftOutlined />} onClick={() => navigate('/assets')}>Quay lại</Button>
        {has('allocate') && canCheckout && <Button type="primary" icon={<SendOutlined />} onClick={() => setAllocOpen(true)}>Cấp phát</Button>}
        {has('edit') && canEdit && <Button icon={<EditOutlined />} onClick={() => navigate(`/assets/${id}/edit`)}>Sửa</Button>}
        {has('archive') && canEdit && (
          <Button danger icon={<InboxOutlined />} onClick={() => setArchiveOpen(true)}>Lưu trữ</Button>
        )}
        {has('recall') && canCheckin && <Button type="primary" danger icon={<RollbackOutlined />} onClick={() => setRecallOpen(true)}>Thu hồi</Button>}
        {status !== 'Archived' && canAudit && <Button icon={<AuditOutlined />} onClick={handleAudit}>Kiểm kê</Button>}
      </Space>

      {/* Lifecycle Steps */}
      <Card title="Vòng đời" style={{ marginBottom: 16 }}>
        <Steps size="small" current={stepIndex} items={LIFECYCLE_STEPS.map((step, i) => ({
          title: step.title,
          description: step.description,
          status: i < stepIndex ? 'finish' : i === stepIndex ? 'process' : 'wait',
        }))} />
        <div style={{ marginTop: 12, display: 'flex', gap: 8, flexWrap: 'wrap' }}>
          <Tag color={ASSET_STATUS_COLORS[status] ?? 'default'}>{ASSET_STATUS_LABELS[status]}</Tag>
          {!asset.isConfirmed && <Tag color="warning">Chưa xác nhận</Tag>}
        </div>
        {status === 'Archived' && <Alert type="error" showIcon style={{ marginTop: 12, borderRadius: 8 }} title="Tài sản đã kết thúc vòng đời (Đã thu hồi). Chỉ xem, không thể sửa đổi." />}
      </Card>

      {/* Asset Information */}
      <Card title={`${asset.assetTag} — ${asset.name}`} style={{ marginBottom: 16 }}>
        <Descriptions bordered size="small" column={2}>
          <Descriptions.Item label="Mã tài sản">{asset.assetTag}</Descriptions.Item>
          <Descriptions.Item label="Serial">{asset.serial || '-'}</Descriptions.Item>
          <Descriptions.Item label="Model">{asset.model?.name || '-'}</Descriptions.Item>
          <Descriptions.Item label="Danh mục">{asset.category ? <Tag color={asset.category.tagColor}>{asset.category.name}</Tag> : '-'}</Descriptions.Item>
          <Descriptions.Item label="Hãng sản xuất">{asset.manufacturer?.name || '-'}</Descriptions.Item>
          <Descriptions.Item label="Nhà cung cấp">{asset.supplier?.name || '-'}</Descriptions.Item>
          <Descriptions.Item label="Công ty">{asset.company?.name || '-'}</Descriptions.Item>
          <Descriptions.Item label="Vị trí">{asset.location?.name || '-'}</Descriptions.Item>
          <Descriptions.Item label="Giá mua">{asset.purchaseCost ? `${asset.purchaseCost.toLocaleString('vi-VN')} VND` : '-'}</Descriptions.Item>
          <Descriptions.Item label="Ngày mua">{formatDate(asset.purchaseDate)}</Descriptions.Item>
          <Descriptions.Item label="Bảo hành">{asset.warrantyMonths ? `${asset.warrantyMonths} tháng` : '-'}</Descriptions.Item>
          <Descriptions.Item label="Số đơn hàng">{asset.orderNumber || '-'}</Descriptions.Item>
          <Descriptions.Item label="Ghi chú" span={2}>{asset.notes || '-'}</Descriptions.Item>
        </Descriptions>
      </Card>

      {/* Allocation / Current State */}
      <Card title="Tình trạng cấp phát" style={{ marginBottom: 16 }}>
        {status === 'Pending' && <Empty description="Chưa được cấp phát — sẵn sàng để cấp phát" />}
        {status === 'Deployed' && asset.assignedTo && (
          <Descriptions bordered size="small" column={1}>
            <Descriptions.Item label="Đối tượng">
              <Space>
                {targetLabel && (
                  <Tag color="blue" icon={asset.assignedTo.type === 'user' ? <UserOutlined /> : asset.assignedTo.type === 'department' ? <TeamOutlined /> : <ClusterOutlined />}>
                    {targetLabel}
                  </Tag>
                )}
                <Text>{assignedName}</Text>
              </Space>
            </Descriptions.Item>
            {asset.assignedTo.type === 'systemPosition' && (
              <Descriptions.Item label="Vị trí lắp đặt">{asset.location?.name || '-'}</Descriptions.Item>
            )}
            <Descriptions.Item label="Ngày cấp phát">{formatDate(asset.lastCheckout)}</Descriptions.Item>
          </Descriptions>
        )}
        {status === 'Archived' && (
          <Descriptions bordered size="small" column={1}>
            <Descriptions.Item label="Vị trí thu hồi">{asset.location?.name || '-'}</Descriptions.Item>
            <Descriptions.Item label="Ngày thu hồi">{formatDate(asset.lastCheckin)}</Descriptions.Item>
          </Descriptions>
        )}

      <Card title="License đang sử dụng" style={{ marginBottom: 16 }}>
        <LicenseUsageTable scope={{ type: 'asset', id: id! }} />
      </Card>

      </Card>

      <Card title="Bảo trì" style={{ marginBottom: 16 }}>
        <AssetMaintenanceSection assetId={id!} />
      </Card>

      <Card title="Lịch sử" style={{ marginBottom: 16 }}>
        <ActionLogTable
          actionRef={actionLogRef}
          params={actionLogParams}
          request={async () => {
            try {
              const res = await apiClient.get('/action-logs', { params: { itemType: 1, itemId: id } });
              const data = (res.data?.data ?? []) as ActionLogRow[];
              return { data, success: true, total: data.length };
            } catch (err) {
              console.error('Failed to load action logs:', err);
              return { data: [], success: false, total: 0 };
            }
          }}
        />
      </Card>

      <AssetAllocationModal open={allocOpen} asset={asset} onClose={() => setAllocOpen(false)} onSuccess={handleAllocSuccess} />
      <AssetRecallModal open={recallOpen} asset={asset} onClose={() => setRecallOpen(false)} onSuccess={handleRecallSuccess} />
      <AssetArchiveModal open={archiveOpen} asset={asset} onClose={() => setArchiveOpen(false)} onSuccess={handleArchiveSuccess} />
    </div>
  );
};

export default AssetDetailPage;
