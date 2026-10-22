import { useEffect, useState, useCallback, useRef, useMemo } from 'react';
import { useParams, useNavigate } from 'react-router-dom';
import { Descriptions, Tag, Spin, Button, Space, Card, App, Steps, Alert, Empty, Result, Tabs, Typography } from 'antd';
import {
  ArrowLeftOutlined, EditOutlined, InboxOutlined, CheckSquareOutlined,
  RollbackOutlined, SendOutlined, UndoOutlined, UserOutlined, TeamOutlined, ClusterOutlined, PrinterOutlined,
} from '@ant-design/icons';
import type { ActionType } from '@ant-design/pro-components';
import apiClient from '../../../services/api-client';
import ActionLogTable, { type ActionLogRow } from '../../../shared/components/ActionLogTable';
import ListRowActions from '../../../shared/components/list/ListRowActions';
import AssetMaintenanceSection from '../components/AssetMaintenanceSection';
import AssetTimelineTab from '../components/AssetTimelineTab';
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
import { formatNumber } from '../../../utils/format';

const { Title, Text } = Typography;

const TARGET_TYPE_LABELS: Record<string, string> = {
  user: 'Người dùng',
  department: 'Phòng ban',
  systemPosition: 'Hệ thống',
};

const LIFECYCLE_STEPS: Array<{ title: string; description: string }> = [
  { title: 'Chờ cấp phát', description: 'Sẵn sàng cấp phát' },
  { title: 'Đang sử dụng', description: 'Đã cấp phát' },
  { title: 'Đã lưu trữ', description: 'Đã lưu trữ' },
];

function lifecycleStepIndex(status: AssetStatus): number {
  return status === 'Pending' ? 0 : status === 'Deployed' ? 1 : 2;
}

const AssetDetailPage: React.FC = () => {
  // [FE-R6] message lấy từ App.useApp() (context theme) thay vì static import.
  const { message } = App.useApp();
  const { id } = useParams<{ id: string }>();
  const navigate = useNavigate();
  const [asset, setAsset] = useState<AssetDetailDto | null>(null);
  const [loading, setLoading] = useState(true);
  const [loadError, setLoadError] = useState<string | null>(null);
  const [allocOpen, setAllocOpen] = useState(false);
  const [recallOpen, setRecallOpen] = useState(false);
  const [archiveOpen, setArchiveOpen] = useState(false);
  const [refreshKey, setRefreshKey] = useState(0);
  const actionLogRef = useRef<ActionType>(null);
  // ST6b — permission gating (mirrors backend [Authorize(Policy=...)]).
  const canEdit = usePermission('assets.edit');
  const canCheckout = usePermission('assets.checkout');
  const canCheckin = usePermission('assets.checkin');
  // Stable identity → ProTable only reloads when refreshKey actually changes.
  const actionLogParams = useMemo(() => ({ refreshKey }), [refreshKey]);

  const loadAsset = useCallback(() => {
    if (!id) return;
    setLoading(true);
    setLoadError(null);
    apiClient.get(`/assets/${id}`)
      .then(r => setAsset(r.data.data))
      .catch((err: unknown) => {
        const e = err as { response?: { status?: number; data?: { message?: string } } };
        setAsset(null);
        setLoadError(e?.response?.status === 404
          ? 'Không tìm thấy tài sản hoặc tài sản nằm ngoài phạm vi công ty của bạn.'
          : (e?.response?.data?.message || 'Không thể tải thông tin tài sản.'));
      })
      .finally(() => setLoading(false));
  }, [id]);

  useEffect(() => { loadAsset(); }, [loadAsset]);

  const handleLifecycleSuccess = () => { setRefreshKey(k => k + 1); loadAsset(); };

  // Modal submit handlers: close the modal first, then refresh asset data + action log table.
  const handleAllocSuccess = () => { setAllocOpen(false); handleLifecycleSuccess(); };
  const handleRecallSuccess = () => { setRecallOpen(false); handleLifecycleSuccess(); };
  const handleArchiveSuccess = () => { setArchiveOpen(false); handleLifecycleSuccess(); };

  const handleConfirm = async () => {
    try { await apiClient.post(`/api/v1/assets/${id}/confirm`); message.success('Đã xác nhận tài sản'); handleLifecycleSuccess(); }
    catch { message.error('Xác nhận thất bại'); }
  };

  const handleUnarchive = async () => {
    const locationId = asset?.location?.id;
    if (!locationId) {
      message.error('Cần có vị trí lưu kho trước khi mở lại tài sản');
      return;
    }
    try {
      await apiClient.post(`/api/v1/assets/${id}/unarchive`, { locationId });
      message.success('Đã mở lại tài sản');
      handleLifecycleSuccess();
    } catch {
      message.error('Mở lại thất bại');
    }
  };

  if (loading) return <Spin size="large" />;
  if (loadError || !asset) {
    return (
      <Result
        status="404"
        title="Không tìm thấy tài sản"
        subTitle={loadError ?? 'Tài sản không tồn tại hoặc đã bị xóa.'}
        extra={[
          <Button key="back" onClick={() => navigate('/assets')}>Danh sách tài sản</Button>,
          <Button key="home" type="primary" onClick={() => navigate('/dashboard')}>Về tổng quan</Button>,
        ]}
      />
    );
  }

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
      <div style={{ display: 'flex', alignItems: 'center', gap: 12, marginBottom: 16, flexWrap: 'wrap' }}>
        <Button icon={<ArrowLeftOutlined />} onClick={() => navigate('/assets')}>Quay lại</Button>
        <div style={{ minWidth: 0 }}>
          <Title level={4} style={{ margin: 0 }}>{asset.name}</Title>
          <Space size={8} wrap>
            <Text code>{asset.assetTag}</Text>
            <Tag color={ASSET_STATUS_COLORS[status] ?? 'default'}>{ASSET_STATUS_LABELS[status]}</Tag>
            {!asset.isConfirmed && <Tag color="warning">Chưa xác nhận</Tag>}
          </Space>
        </div>
        <div style={{ flex: 1 }} />
        <Button icon={<PrinterOutlined />} onClick={() => navigate(`/assets/labels/print?ids=${encodeURIComponent(asset.id)}`)}>
          In tem
        </Button>
        <ListRowActions
          primary={
            !asset.isConfirmed && has('confirm') && canEdit
              ? { key: 'confirm', label: 'Xác nhận', icon: <CheckSquareOutlined />, onClick: handleConfirm }
              : status === 'Deployed' && has('recall') && canCheckin
                ? { key: 'recall', label: 'Thu hồi', icon: <RollbackOutlined />, onClick: () => setRecallOpen(true) }
                : status === 'Pending' && has('allocate') && canCheckout
                  ? { key: 'allocate', label: 'Cấp phát', icon: <SendOutlined />, onClick: () => setAllocOpen(true) }
                  : status === 'Archived' && has('unarchive') && canEdit
                    ? { key: 'unarchive', label: 'Mở lại', icon: <UndoOutlined />, onClick: handleUnarchive }
                    : null
          }
          menuItems={[
            ...(has('edit') && canEdit ? [{ key: 'edit', label: 'Sửa', icon: <EditOutlined />, onClick: () => navigate(`/assets/${id}/edit`) }] : []),
            ...(has('archive') && canEdit ? [{ key: 'archive', label: 'Lưu trữ', icon: <InboxOutlined />, danger: true, onClick: () => setArchiveOpen(true) }] : []),
          ]}
        />
      </div>

      {/* Lifecycle Steps */}
      <Card title="Vòng đời" style={{ marginBottom: 16 }}>
        <Steps size="small" current={stepIndex} items={LIFECYCLE_STEPS.map((step, i) => ({
          title: step.title,
          // antd 6: Steps items.description deprecated → content (runtime warning xác nhận 2026-08-22)
          content: step.description,
          status: i < stepIndex ? 'finish' : i === stepIndex ? 'process' : 'wait',
        }))} />
        <div style={{ marginTop: 12, display: 'flex', gap: 8, flexWrap: 'wrap' }}>
          <Tag color={ASSET_STATUS_COLORS[status] ?? 'default'}>{ASSET_STATUS_LABELS[status]}</Tag>
          {!asset.isConfirmed && <Tag color="warning">Chưa xác nhận</Tag>}
        </div>
        {status === 'Archived' && <Alert type="error" showIcon style={{ marginTop: 12, borderRadius: 8 }} title="Tài sản đã kết thúc vòng đời (Đã lưu trữ). Chỉ xem, không thể sửa đổi." />}
      </Card>

      {/* Asset Information */}
      <Card title={`${asset.assetTag} — ${asset.name}`} style={{ marginBottom: 16 }}>
        {/* column responsive (bug: column={2} cứng trên mobile → cell content bị nén dọc) */}
        <Descriptions bordered size="small" column={{ xs: 1, sm: 2 }}>
          <Descriptions.Item label="Mã tài sản">{asset.assetTag}</Descriptions.Item>
          <Descriptions.Item label="Số serial">{asset.serial || '-'}</Descriptions.Item>
          <Descriptions.Item label="Model">{asset.model?.name || '-'}</Descriptions.Item>
          <Descriptions.Item label="Danh mục">{asset.category ? <Tag color={asset.category.tagColor}>{asset.category.name}</Tag> : '-'}</Descriptions.Item>
          <Descriptions.Item label="Hãng sản xuất">{asset.manufacturer?.name || '-'}</Descriptions.Item>
          <Descriptions.Item label="Nhà cung cấp">{asset.supplier?.name || '-'}</Descriptions.Item>
          <Descriptions.Item label="Công ty">{asset.company?.name || '-'}</Descriptions.Item>
          <Descriptions.Item label="Vị trí">{asset.location?.name || '-'}</Descriptions.Item>
          <Descriptions.Item label="Giá mua">{asset.purchaseCost ? `${formatNumber(asset.purchaseCost)} VND` : '-'}</Descriptions.Item>
          <Descriptions.Item label="Ngày mua">{formatDate(asset.purchaseDate)}</Descriptions.Item>
          <Descriptions.Item label="Bảo hành">{asset.warrantyMonths ? `${asset.warrantyMonths} tháng` : '-'}</Descriptions.Item>
          <Descriptions.Item label="Số đơn hàng">{asset.orderNumber || '-'}</Descriptions.Item>
          <Descriptions.Item label="Ghi chú" span={{ xs: 1, sm: 2 }}>{asset.notes || '-'}</Descriptions.Item>
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

      <Card title="Bản quyền đang sử dụng" style={{ marginBottom: 16 }}>
        <LicenseUsageTable scope={{ type: 'asset', id: id! }} />
      </Card>

      </Card>

      <Card title="Bảo trì" style={{ marginBottom: 16 }}>
        <AssetMaintenanceSection assetId={id!} />
      </Card>

      {/* Lịch sử (ActionLog thô) + Lý lịch thiết bị [LL-4] — 2 tab; nội dung Lịch sử GIỮ NGUYÊN
          (cùng ActionLogTable, cùng request/params), Lý lịch là dòng thời gian mới.
          [LL-4b] Tab MẶC ĐỊNH là "Lý lịch" (defaultActiveKey="timeline"). */}
      <Card style={{ marginBottom: 16 }}>
        <Tabs
          defaultActiveKey="timeline"
          items={[
            {
              key: 'history',
              label: 'Lịch sử',
              children: (
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
              ),
            },
            {
              key: 'timeline',
              label: 'Lý lịch',
              // Chỉ mount khi người dùng mở tab (antd Tabs render lười) → không gọi API timeline
              // khi chưa cần.
              children: <AssetTimelineTab assetId={id!} />,
            },
          ]}
        />
      </Card>

      <AssetAllocationModal open={allocOpen} asset={asset} onClose={() => setAllocOpen(false)} onSuccess={handleAllocSuccess} />
      <AssetRecallModal open={recallOpen} asset={asset} onClose={() => setRecallOpen(false)} onSuccess={handleRecallSuccess} />
      <AssetArchiveModal open={archiveOpen} asset={asset} onClose={() => setArchiveOpen(false)} onSuccess={handleArchiveSuccess} />
    </div>
  );
};

export default AssetDetailPage;
