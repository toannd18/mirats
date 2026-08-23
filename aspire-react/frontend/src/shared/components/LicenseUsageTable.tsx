import { Empty, Table, Tag, Card, Typography } from 'antd';
import { licensesApi, type LicenseUsageRow } from '../../features/license/services/licenses.service';
import { useEffect, useState } from 'react';
import { formatDate, formatDateTime } from '../../utils/format';
import { useIsMobile } from '../../hooks/useIsMobile';

const { Text } = Typography;

interface LicenseUsageTableProps {
  /** assetId or systemInfoId — the scope this section is bound to. */
  scope: { type: 'asset' | 'system' | 'user'; id: string };
}

/**
 * Small read-only table of licenses whose seat is currently checked out to an Asset or a System
 * (SystemInfo). Used by AssetDetailPage, UserDetailPage and SystemDetailPage (tab License).
 * Mobile (T-RESP4): Card list thay Table — responsive tại ĐÂY, mọi trang nhúng cùng hưởng.
 */
export default function LicenseUsageTable({ scope }: LicenseUsageTableProps) {
  const [data, setData] = useState<LicenseUsageRow[]>([]);
  const [loading, setLoading] = useState(false);
  const isMobile = useIsMobile();

  useEffect(() => {
    if (!scope.id) return;
    setLoading(true);
    const req = scope.type === 'asset' ? licensesApi.forAsset(scope.id) : scope.type === 'system' ? licensesApi.forSystem(scope.id) : licensesApi.forUser(scope.id);
    req
      .then(r => setData((r.data?.data ?? []) as LicenseUsageRow[]))
      .catch(() => setData([]))
      .finally(() => setLoading(false));
  }, [scope.type, scope.id]);

  const columns = [
    {
      title: 'License', key: 'license',
      render: (_: unknown, r: LicenseUsageRow) => (
        <span style={{ fontWeight: 600 }}>{r.licenseName} <Tag color="blue">seat #{r.seatNumber}</Tag></span>
      ),
    },
    {
      title: 'Ngày cấp', key: 'assignedAt', width: 150,
      render: (_: unknown, r: LicenseUsageRow) => formatDateTime(r.assignedAt),
    },
    {
      title: 'Cảnh báo hết hạn', key: 'expiry', width: 170,
      render: (_: unknown, r: LicenseUsageRow) => {
        if (r.isExpired) return <Tag color="red">Hết hạn {formatDate(r.expirationDate)}</Tag>;
        if (r.expiringSoon) return <Tag color="orange">Sắp hết hạn {formatDate(r.expirationDate)}</Tag>;
        return formatDate(r.expirationDate);
      },
    },
    { title: 'Ghi chú', key: 'note', width: 150, render: (_: unknown, r: LicenseUsageRow) => r.note || '-' },
  ];

  if (!loading && data.length === 0) {
    return <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} description="Chưa có license nào đang gán" />;
  }

  // ─── Mobile (T-RESP4): Card list — cùng 1 fetch, không đụng caller ───
  if (isMobile) {
    return (
      <div>
        {data.map(r => (
          <Card key={`${r.licenseId}-${r.seatNumber}`} size="small" style={{ borderRadius: 10, marginBottom: 12 }}>
            <div style={{ marginBottom: 8 }}>
              <Text strong style={{ fontSize: 14, marginRight: 8 }}>{r.licenseName}</Text>
              <Tag color="blue" style={{ marginInlineEnd: 0 }}>seat #{r.seatNumber}</Tag>
            </div>
            <div style={{ display: 'grid', gridTemplateColumns: 'auto 1fr', gap: '4px 10px' }}>
              <Text type="secondary" style={{ fontSize: 12 }}>Ngày cấp</Text>
              <Text style={{ fontSize: 13 }}>{formatDateTime(r.assignedAt)}</Text>
              <Text type="secondary" style={{ fontSize: 12 }}>Hết hạn</Text>
              <Text style={{ fontSize: 13 }}>
                {r.isExpired
                  ? <Tag color="red" style={{ margin: 0 }}>Hết hạn {formatDate(r.expirationDate)}</Tag>
                  : r.expiringSoon
                    ? <Tag color="orange" style={{ margin: 0 }}>Sắp hết hạn {formatDate(r.expirationDate)}</Tag>
                    : formatDate(r.expirationDate)}
              </Text>
              <Text type="secondary" style={{ fontSize: 12 }}>Ghi chú</Text>
              <Text style={{ fontSize: 13 }}>{r.note || '-'}</Text>
            </div>
          </Card>
        ))}
      </div>
    );
  }

  return (
    <Table
      rowKey={(r) => `${r.licenseId}-${r.seatNumber}`}
      columns={columns}
      dataSource={data}
      loading={loading}
      size="small"
      pagination={false}
      scroll={{ x: 'max-content' }}
    />
  );
}
