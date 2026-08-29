import { useEffect, useState } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import { Empty, Table, Tag } from 'antd';
import dayjs from 'dayjs';
import apiClient from '../../../services/api-client';

interface CampaignRow {
  id: string;
  systemInfoName: string;
  versionNumber: number;
  startDate?: string | null;
  endDate?: string | null;
  batchNumber?: string | null;
  status: string;
  snapshotCount: number;
  resultsCount: number;
}

const STATUS_TAG: Record<string, { color: string; label: string }> = {
  InProgress: { color: 'processing', label: 'Đang thực hiện' },
  Completed: { color: 'success', label: 'Hoàn thành' },
};

/**
 * MC-6 — Bảng "Lịch sử bảo dưỡng" cho MỘT hệ thống (tab trong SystemDetailPage).
 * Danh sách Campaign đã/c đang thực hiện; click mở trang chi tiết kết quả checklist.
 */
export default function CampaignHistoryTable({ systemInfoId }: { systemInfoId: string }) {
  const navigate = useNavigate();
  const [rows, setRows] = useState<CampaignRow[]>([]);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    let alive = true;
    setLoading(true);
    apiClient.get('/maintenance/campaigns', { params: { systemInfoId } })
      .then(res => { if (alive) setRows((res.data?.data ?? []) as CampaignRow[]); })
      .catch(() => { if (alive) setRows([]); })
      .finally(() => { if (alive) setLoading(false); });
    return () => { alive = false; };
  }, [systemInfoId]);

  return (
    <Table<CampaignRow>
      rowKey="id"
      size="small"
      loading={loading}
      pagination={false}
      scroll={{ x: 'max-content' }}
      dataSource={rows}
      locale={{ emptyText: <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} description="Hệ thống chưa có đợt bảo dưỡng nào" /> }}
      columns={[
        {
          title: 'Mã đợt', key: 'batch', width: 150,
          render: (_, r) => <Link to={`/maintenance/campaigns/${r.id}`}>{r.batchNumber || '(không mã)'}</Link>,
        },
        {
          title: 'Version', dataIndex: 'versionNumber', key: 'versionNumber', width: 90,
          render: (v: number) => <Tag color="blue">v{v}</Tag>,
        },
        {
          title: 'Trạng thái', dataIndex: 'status', key: 'status', width: 130,
          render: (_, r) => {
            const s = STATUS_TAG[r.status] ?? { color: 'default', label: r.status };
            return <Tag color={s.color}>{s.label}</Tag>;
          },
        },
        {
          title: 'Thời gian', key: 'time', width: 220,
          render: (_, r) => `${dayjs(r.startDate).format('DD/MM/YYYY')} → ${r.endDate ? dayjs(r.endDate).format('DD/MM/YYYY') : '...'}`,
        },
        {
          // [MC-9] resultsCount = dòng kết quả theo tiêu chuẩn (thiết bị × tiêu chuẩn)
          title: 'Kết quả', key: 'progress', width: 140,
          render: (_, r) => `${r.resultsCount} dòng kết quả`,
        },
        {
          title: '', key: 'actions', width: 90,
          render: (_, r) => (
            <a onClick={() => navigate(`/maintenance/campaigns/${r.id}`)}>Chi tiết</a>
          ),
        },
      ]}
    />
  );
}
