import { useCallback, useEffect, useState } from 'react';
import { useNavigate, useParams } from 'react-router-dom';
import {
  App, Button, Card, Descriptions, Empty, Space, Spin, Table, Tag, Tooltip, Typography,
} from 'antd';
import {
  ArrowLeftOutlined, DeleteOutlined, EditOutlined, ExportOutlined, RollbackOutlined,
} from '@ant-design/icons';
import { licensesApi, type LicenseDetailDto, type LicenseSeatDto } from '../services/licenses.service';
import LicenseCheckoutModal from '../components/LicenseCheckoutModal';
import ListRowActions from '../../../shared/components/list/ListRowActions';
import { usePermission } from '../../../hooks/usePermission';
import { statusColors } from '../../../theme/designTokens';
import { formatDate, formatDateTime } from '../../../utils/format';

const { Title, Text } = Typography;

function AssignedCell({ seat }: { seat: LicenseSeatDto }) {
  if (!seat.assigned) return <Text type="secondary">—</Text>;
  if (seat.targetType === 'User') {
    return <Space size={4}><Tag color="geekblue" style={{ marginInlineEnd: 0 }}>Người dùng</Tag>{seat.user?.name}</Space>;
  }
  if (seat.targetType === 'Asset') {
    return <Space size={4}><Tag color="cyan" style={{ marginInlineEnd: 0 }}>Tài sản</Tag>{seat.asset?.name} ({seat.asset?.assetTag})</Space>;
  }
  return (
    <Space size={4}>
      <Tag color="purple" style={{ marginInlineEnd: 0 }}>Hệ thống</Tag>
      {seat.systemInfo?.name}
    </Space>
  );
}

export default function LicenseDetailPage() {
  const { id } = useParams<{ id: string }>();
  const navigate = useNavigate();
  const { message, modal } = App.useApp();
  const [license, setLicense] = useState<LicenseDetailDto | null>(null);
  const [loading, setLoading] = useState(true);
  const [checkoutSeat, setCheckoutSeat] = useState<{ id: string; seatNumber: number } | null>(null);
  const [checkoutOpen, setCheckoutOpen] = useState(false);

  const canEdit = usePermission('licenses.edit');
  const canDelete = usePermission('licenses.delete');
  const canCheckout = usePermission('licenses.checkout');

  const load = useCallback(async () => {
    if (!id) return;
    setLoading(true);
    try {
      const res = await licensesApi.get(id);
      setLicense(res.data.data as LicenseDetailDto);
    } catch {
      void message.error('Không thể tải chi tiết bản quyền');
    } finally {
      setLoading(false);
    }
  }, [id, message]);

  useEffect(() => { void load(); }, [load]);

  if (loading) return <Spin style={{ display: 'block', margin: '80px auto' }} />;
  if (!license) return <Empty description="Không tìm thấy bản quyền" style={{ marginTop: 80 }} />;

  const available = license.seats - license.assignedSeats;
  const statusTag = license.isExpired
    ? <Tag color="red">Hết hạn {formatDate(license.expirationDate)}</Tag>
    : license.expiringSoon
      ? <Tag color="orange">Sắp hết hạn {formatDate(license.expirationDate)}</Tag>
      : <Tag color="green">Đang hiệu lực</Tag>;

  const handleCheckin = (seat: LicenseSeatDto) => {
    modal.confirm({
      title: `Thu hồi ghế #${seat.seatNumber}?`,
      content: 'Ghế sẽ trở lại trạng thái trống và có thể cấp lại cho đối tượng khác.',
      okText: 'Thu hồi',
      cancelText: 'Hủy',
      onOk: async () => {
        try {
          await licensesApi.checkin(license.id, seat.id);
          void message.success('Đã thu hồi ghế');
          await load();
        } catch (err: unknown) {
          const e = err as { response?: { data?: { message?: string } } };
          void message.error(e?.response?.data?.message || 'Không thể thu hồi ghế');
        }
      },
    });
  };

  const seatColumns = [
    { title: 'Số thứ tự', key: 'seatNumber', width: 90, render: (_: unknown, s: LicenseSeatDto) => `#${s.seatNumber}` },
    {
      title: 'Trạng thái', key: 'assigned', width: 100,
      render: (_: unknown, s: LicenseSeatDto) => s.assigned ? <Tag color="blue">Đã cấp</Tag> : <Tag>Trống</Tag>,
    },
    { title: 'Đang gán cho', key: 'target', render: (_: unknown, s: LicenseSeatDto) => <AssignedCell seat={s} /> },
    { title: 'Ngày cấp', key: 'assignedAt', width: 150, render: (_: unknown, s: LicenseSeatDto) => formatDateTime(s.assignedAt) },
    { title: 'Ghi chú', key: 'note', width: 150, render: (_: unknown, s: LicenseSeatDto) => s.note || '—' },
    {
      title: 'Thao tác', key: 'actions', width: 120,
      render: (_: unknown, s: LicenseSeatDto) => s.assigned
        ? (
          <Tooltip title={license.reassignable ? 'Thu hồi ghế' : 'Bản quyền không cho phép thu hồi'}>
            <Button size="small" icon={<RollbackOutlined />} disabled={!license.reassignable}
              onClick={() => handleCheckin(s)}>Thu hồi</Button>
          </Tooltip>
        )
        : (
          <Button size="small" icon={<ExportOutlined />}
            onClick={() => setCheckoutSeat({ id: s.id, seatNumber: s.seatNumber })}>Cấp phát</Button>
        ),
    },
  ];

  return (
    <div>
      <div style={{ display: 'flex', alignItems: 'center', gap: 12, marginBottom: 16, flexWrap: 'wrap' }}>
        <Button icon={<ArrowLeftOutlined />} onClick={() => navigate('/licenses')}>Quay lại</Button>
        <div style={{ minWidth: 0 }}>
          <Title level={4} style={{ margin: 0 }}>{license.name}</Title>
          <Text type="secondary">{license.serial || '—'}</Text>
        </div>
        {statusTag}
        <div style={{ flex: 1 }} />
        <ListRowActions
          primary={canCheckout && available > 0 ? {
            key: 'checkout', label: 'Cấp phát', icon: <ExportOutlined />,
            onClick: () => setCheckoutOpen(true),
          } : null}
          menuItems={[
            ...(canEdit ? [{ key: 'edit', label: 'Sửa', icon: <EditOutlined />, onClick: () => navigate(`/licenses/${license.id}/edit`) }] : []),
            ...(canDelete ? [{
              key: 'delete', label: 'Xóa', icon: <DeleteOutlined />, danger: true,
              onClick: () => modal.confirm({
                title: 'Xóa bản quyền?',
                content: 'Hành động này không thể hoàn tác.',
                okText: 'Xóa', cancelText: 'Hủy', okButtonProps: { danger: true },
                onOk: async () => {
                  try {
                    await licensesApi.delete(license.id);
                    void message.success('Đã xóa bản quyền');
                    navigate('/licenses');
                  } catch (err: unknown) {
                    const e = err as { response?: { data?: { message?: string } } };
                    void message.error(e?.response?.data?.message || 'Không thể xóa bản quyền');
                  }
                },
              }),
            }] : []),
          ]}
        />
      </div>

      <Card title="Thông tin bản quyền" size="small" style={{ marginBottom: 16 }}>
        <Descriptions bordered size="small" column={{ xs: 1, sm: 2 }}>
          <Descriptions.Item label="Tên bản quyền">{license.name}</Descriptions.Item>
          <Descriptions.Item label="Số serial">{license.serial || '—'}</Descriptions.Item>
          <Descriptions.Item label="Danh mục">{license.category?.name ? <Tag color="blue">{license.category.name}</Tag> : '—'}</Descriptions.Item>
          <Descriptions.Item label="Công ty">{license.company?.name || '—'}</Descriptions.Item>
          <Descriptions.Item label="Tổng ghế">{license.seats}</Descriptions.Item>
          <Descriptions.Item label="Đã cấp">{license.assignedSeats}</Descriptions.Item>
          <Descriptions.Item label="Còn trống">
            <Text strong style={{ color: available > 0 ? statusColors.ready : statusColors.overdue }}>{available}</Text>
          </Descriptions.Item>
          <Descriptions.Item label="Có thể cấp lại">{license.reassignable ? 'Có' : 'Không'}</Descriptions.Item>
          <Descriptions.Item label="Ngày hết hạn">{formatDate(license.expirationDate)}</Descriptions.Item>
          <Descriptions.Item label="Ngày mua">{formatDate(license.purchaseDate)}</Descriptions.Item>
          <Descriptions.Item label="Nhà cung cấp">{license.supplier?.name || '—'}</Descriptions.Item>
          <Descriptions.Item label="Nhà sản xuất">{license.manufacturer?.name || '—'}</Descriptions.Item>
          <Descriptions.Item label="Ghi chú" span={{ xs: 1, sm: 2 }}>{license.notes || '—'}</Descriptions.Item>
        </Descriptions>
      </Card>

      <Card title="Ghế bản quyền" size="small">
        <Table
          rowKey="id"
          columns={seatColumns}
          dataSource={license.seatDetails ?? []}
          size="small"
          pagination={false}
          scroll={{ x: 'max-content' }}
        />
      </Card>

      <LicenseCheckoutModal
        open={checkoutOpen || !!checkoutSeat}
        licenseId={license.id}
        licenseName={license.name}
        seatId={checkoutSeat?.id ?? null}
        seatNumber={checkoutSeat?.seatNumber ?? null}
        companyId={license.companyId}
        onClose={() => { setCheckoutOpen(false); setCheckoutSeat(null); }}
        onSaved={() => { setCheckoutOpen(false); setCheckoutSeat(null); void load(); }}
      />
    </div>
  );
}
