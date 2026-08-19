import { Empty, Table, Tag } from 'antd';
import { licensesApi, type LicenseUsageRow } from '../../features/license/services/licenses.service';
import { useEffect, useState } from 'react';
import { formatDate, formatDateTime } from '../../utils/format';

interface LicenseUsageTableProps {
  /** assetId or systemInfoId — the scope this section is bound to. */
  scope: { type: 'asset' | 'system' | 'user'; id: string };
}

/**
 * Small read-only table of licenses whose seat is currently checked out to an Asset or a System
 * (SystemInfo). Used by AssetDetailPage, UserDetailPage and SystemDetailPage (tab License).
 */
export default function LicenseUsageTable({ scope }: LicenseUsageTableProps) {
  const [data, setData] = useState<LicenseUsageRow[]>([]);
  const [loading, setLoading] = useState(false);

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
