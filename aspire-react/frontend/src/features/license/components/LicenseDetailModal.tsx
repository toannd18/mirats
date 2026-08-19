import { useEffect, useState } from 'react';
import { App, Button, Descriptions, Modal, Space, Spin, Table, Tag, Tooltip, Typography } from 'antd';
import { ExportOutlined, RollbackOutlined } from '@ant-design/icons';
import { licensesApi, type LicenseDetailDto, type LicenseSeatDto } from '../services/licenses.service';
import LicenseCheckoutModal from './LicenseCheckoutModal';
import { statusColors } from '../../../theme/designTokens';
import { formatDate, formatDateTime } from '../../../utils/format';

const { Text } = Typography;

interface LicenseDetailModalProps {
  open: boolean;
  licenseId?: string | null;
  onClose: () => void;
  /** Called after a checkout/checkin so the parent list can refresh. */
  onSaved: () => void;
}

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

export default function LicenseDetailModal({ open, licenseId, onClose, onSaved }: LicenseDetailModalProps) {
  const { message, modal } = App.useApp();
  const [loading, setLoading] = useState(false);
  const [license, setLicense] = useState<LicenseDetailDto | null>(null);
  const [checkoutSeat, setCheckoutSeat] = useState<{ id: string; seatNumber: number } | null>(null);

  const load = async () => {
    if (!licenseId) return;
    setLoading(true);
    try {
      const res = await licensesApi.get(licenseId);
      setLicense(res.data.data as LicenseDetailDto);
    } catch {
      message.error('Không thể tải chi tiết bản quyền');
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    if (open) void load();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [open, licenseId]);

  const handleCheckin = (seat: LicenseSeatDto) => {
    modal.confirm({
      title: `Thu hồi seat #${seat.seatNumber}?`,
      content: 'Seat sẽ trở lại trạng thái trống và có thể cấp lại cho đối tượng khác.',
      okText: 'Thu hồi',
      cancelText: 'Hủy',
      onOk: async () => {
        try {
          await licensesApi.checkin(licenseId!, seat.id);
          message.success('Đã thu hồi seat');
          void load();
          onSaved();
        } catch (err: unknown) {
          const e = err as { response?: { data?: { message?: string } } };
          message.error(e?.response?.data?.message || 'Không thể thu hồi seat');
        }
      },
    });
  };

  const columns = [
    { title: 'Số thứ tự', key: 'seatNumber', width: 70, render: (_: unknown, s: LicenseSeatDto) => `#${s.seatNumber}` },
    {
      title: 'Trạng thái', key: 'assigned', width: 90,
      render: (_: unknown, s: LicenseSeatDto) => s.assigned ? <Tag color="blue">Đã cấp</Tag> : <Tag>Trống</Tag>,
    },
    { title: 'Đang gán cho', key: 'target', render: (_: unknown, s: LicenseSeatDto) => <AssignedCell seat={s} /> },
    { title: 'Ngày cấp', key: 'assignedAt', width: 150, render: (_: unknown, s: LicenseSeatDto) => formatDateTime(s.assignedAt) },
    { title: 'Ghi chú', key: 'note', width: 140, render: (_: unknown, s: LicenseSeatDto) => s.note || '-' },
    {
      title: 'Thao tác', key: 'actions', width: 120,
      render: (_: unknown, s: LicenseSeatDto) => s.assigned
        ? (
          <Tooltip title={license?.reassignable ? 'Thu hồi seat' : 'License không cho phép thu hồi (Reassignable = false)'}>
            <Button size="small" icon={<RollbackOutlined />} disabled={!license?.reassignable}
              onClick={() => handleCheckin(s)}>Checkin</Button>
          </Tooltip>
        )
        : (
          <Button size="small" icon={<ExportOutlined />}
            onClick={() => setCheckoutSeat({ id: s.id, seatNumber: s.seatNumber })}>Checkout</Button>
        ),
    },
  ];

  const available = license ? license.seats - license.assignedSeats : 0;

  return (
    <Modal
      open={open}
      title={license ? license.name : 'Chi tiết bản quyền'}
      onCancel={onClose}
      footer={[
        <Button key="close" onClick={onClose}>Đóng</Button>,
      ]}
      width={860}
      destroyOnHidden
    >
      <Spin spinning={loading}>
        {license && (
          <>
            <Descriptions column={3} size="small" bordered style={{ marginBottom: 16 }}>
              <Descriptions.Item label="Danh mục">{license.category?.name || '-'}</Descriptions.Item>
              <Descriptions.Item label="Công ty">{license.company?.name || '-'}</Descriptions.Item>
              <Descriptions.Item label="Reassignable">
                {license.reassignable ? <Tag color="green">Có</Tag> : <Tag color="red">Không</Tag>}
              </Descriptions.Item>
              <Descriptions.Item label="Tổng ghế">{license.seats}</Descriptions.Item>
              <Descriptions.Item label="Đã cấp">{license.assignedSeats}</Descriptions.Item>
              <Descriptions.Item label="Còn trống">
                <Text strong style={{ color: available > 0 ? statusColors.ready : statusColors.overdue }}>{available}</Text>
              </Descriptions.Item>
              <Descriptions.Item label="Ngày hết hạn">
                {license.isExpired
                  ? <Tag color="red">Hết hạn {formatDate(license.expirationDate)}</Tag>
                  : license.expiringSoon
                    ? <Tag color="orange">Sắp hết hạn {formatDate(license.expirationDate)}</Tag>
                    : formatDate(license.expirationDate)}
              </Descriptions.Item>
              <Descriptions.Item label="Serial">{license.serial || '-'}</Descriptions.Item>
              <Descriptions.Item label="MinSeats">{license.minSeats ?? '-'}</Descriptions.Item>
            </Descriptions>
            <Table
              rowKey="id"
              columns={columns}
              dataSource={license.seatDetails ?? []}
              size="small"
              pagination={false}
              scroll={{ x: 'max-content' }}
            />
          </>
        )}
      </Spin>
      {license && (
        <LicenseCheckoutModal
          open={!!checkoutSeat}
          licenseId={license.id}
          licenseName={license.name}
          seatId={checkoutSeat?.id ?? null}
          seatNumber={checkoutSeat?.seatNumber ?? null}
          companyId={license.companyId}
          onClose={() => setCheckoutSeat(null)}
          onSaved={() => {
            setCheckoutSeat(null);
            void load();
            onSaved();
          }}
        />
      )}
    </Modal>
  );
}
