import { useEffect, useMemo, useState } from 'react';
import { App, Card, DatePicker, Empty, Select, Space, Typography } from 'antd';
import type { Dayjs } from 'dayjs';
import apiClient from '../../../services/api-client';
import ActionLogTable from '../../../shared/components/ActionLogTable';
import type { ActionLogRow } from '../../../shared/components/ActionLogTable';

const { Title, Text } = Typography;

interface SystemPositionOption {
  id: string;
  code: string;
  name: string;
}

interface SystemInfoOption {
  id: string;
  code: string;
  name: string;
  companyId?: string | null;
  positions: SystemPositionOption[];
}

interface SelectOption {
  label: string;
  value: string;
}

// ActionType enum values (must match Domain/Enums/ActionType.cs)
const ACTION_FILTERS: { label: string; value: number }[] = [
  { label: 'Cấp phát', value: 4 },
  { label: 'Thu hồi', value: 5 },
  { label: 'Lưu trữ', value: 12 },
  { label: 'Mở lại', value: 13 },
];

const SystemHistoryPage: React.FC = () => {
  const { message } = App.useApp();

  const [systems, setSystems] = useState<SystemInfoOption[]>([]);
  const [systemsLoading, setSystemsLoading] = useState(false);
  const [systemId, setSystemId] = useState<string | undefined>(undefined);
  const [positionId, setPositionId] = useState<string | undefined>(undefined);
  const [actionType, setActionType] = useState<number | undefined>(undefined);
  const [dateRange, setDateRange] = useState<[Dayjs | null, Dayjs | null] | null>(null);

  const loadSystems = async () => {
    setSystemsLoading(true);
    try {
      const res = await apiClient.get('/system-infos', { params: { pageSize: 500 } });
      setSystems((res.data?.data ?? []) as SystemInfoOption[]);
    } catch {
      message.error('Không thể tải danh sách hệ thống');
    } finally {
      setSystemsLoading(false);
    }
  };

  useEffect(() => { void loadSystems(); }, []);

  const selectedSystem = systems.find(s => s.id === systemId);

  const positionOptions: SelectOption[] = useMemo(
    () => (selectedSystem?.positions ?? []).map(p => ({ label: `${p.code} — ${p.name}`, value: p.id })),
    [selectedSystem],
  );

  // Identity changes whenever any filter changes → ProTable re-runs request (same pattern as AssetDetailPage).
  const tableParams = useMemo(
    () => ({
      refreshKey: `${systemId ?? ''}|${positionId ?? ''}|${actionType ?? ''}|${dateRange?.[0]?.toISOString() ?? ''}|${dateRange?.[1]?.toISOString() ?? ''}`,
    }),
    [systemId, positionId, actionType, dateRange],
  );

  const handleSystemChange = (value: string) => {
    setSystemId(value);
    setPositionId(undefined); // dependent dropdown must reset when the parent system changes
  };

  const filterLabelStyle: React.CSSProperties = { display: 'block', marginBottom: 4 };

  return (
    <div>
      <Title level={4} style={{ marginTop: 0 }}>Lịch sử hệ thống</Title>

      <Card size="small" style={{ marginBottom: 16 }}>
        <Space size="large" wrap align="end">
          <div>
            <Text strong style={filterLabelStyle}>Hệ thống *</Text>
            <Select
              showSearch
              size="middle"
              style={{ minWidth: 320 }}
              placeholder="Chọn hệ thống..."
              loading={systemsLoading}
              value={systemId}
              onChange={handleSystemChange}
              options={systems.map(s => ({ label: `${s.code} — ${s.name}`, value: s.id }))}
              filterOption={(input, option) => (option?.label as string)?.toLowerCase().includes(input.toLowerCase())}
            />
          </div>

          {systemId && (
            <>
              <div>
                <Text strong style={filterLabelStyle}>Vị trí lắp đặt</Text>
                <Select
                  showSearch
                  size="middle"
                  style={{ minWidth: 240 }}
                  placeholder="Tất cả vị trí"
                  allowClear
                  value={positionId}
                  onChange={setPositionId}
                  options={positionOptions}
                  filterOption={(input, option) => (option?.label as string)?.toLowerCase().includes(input.toLowerCase())}
                />
              </div>
              <div>
                <Text strong style={filterLabelStyle}>Hành động</Text>
                <Select
                  size="middle"
                  style={{ minWidth: 160 }}
                  placeholder="Tất cả hành động"
                  allowClear
                  value={actionType}
                  onChange={setActionType}
                  options={ACTION_FILTERS}
                />
              </div>
              <div>
                <Text strong style={filterLabelStyle}>Khoảng thời gian</Text>
                <DatePicker.RangePicker
                  size="middle"
                  style={{ minWidth: 300 }}
                  value={dateRange}
                  onChange={v => setDateRange(v)}
                />
              </div>
            </>
          )}
        </Space>
      </Card>

      {!systemId ? (
        // Empty state BEFORE a system is chosen — never load all systems' logs by default.
        <Card>
          <Empty
            image={Empty.PRESENTED_IMAGE_SIMPLE}
            description="Chọn một hệ thống để xem lịch sử cấp phát/thu hồi."
          />
        </Card>
      ) : (
        <ActionLogTable
          headerTitle={
            <Text strong>Lịch sử cấp phát/thu hồi — {selectedSystem ? `${selectedSystem.code} — ${selectedSystem.name}` : ''}</Text>
          }
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
                  systemInfoId: systemId,
                  systemPositionId: positionId || undefined,
                  actionType: actionType || undefined,
                  from: dateRange?.[0]?.startOf('day').toISOString(),
                  to: dateRange?.[1]?.endOf('day').toISOString(),
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
      )}
    </div>
  );
};

export default SystemHistoryPage;

