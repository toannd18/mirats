import { useRef } from 'react';
import { Button, Card, Divider, Typography, App } from 'antd';
import { ReloadOutlined } from '@ant-design/icons';
import { ProList, ProTable } from '@ant-design/pro-components';
import type { ActionType, ProColumns } from '@ant-design/pro-components';
import apiClient from '../../../services/api-client';
import { usePermission } from '../../../hooks/usePermission';
import { useIsMobile } from '../../../hooks/useIsMobile';

const { Text } = Typography;

/**
 * DepreciationListPage — trang quản trị cấu hình khấu hao (master-data tham chiếu toàn cục,
 * không company-scoped: bảng depreciations không có cột CompanyId).
 *
 * T-CLEAN1 (2026-08-22): bản cũ là 1 dòng minified 834 byte từ Initial commit (scaffold chưa
 * hoàn thiện — Table thô, không ProTable/toolbar/permission gate). Refactor về đúng chuẩn các
 * trang admin chị em (CompanyListPage pattern): ProTable + request + toolBarRender.
 *
 * Trang CHỈ XEM: backend GET /depreciations hiện là endpoint duy nhất của resource này
 * (read-only, không CRUD — workflow doc §210), nên không có nút create/edit/delete;
 * `depreciations.view` dùng để gate menu/route (App.tsx permMap) và policy backend.
 */
export default function DepreciationListPage() {
  // [FE-R6] message lấy từ App.useApp() (context theme) thay vì static import.
  const { message } = App.useApp();
  const actionRef = useRef<ActionType | null>(null);
  const isMobile = useIsMobile();

  // Gate toolbar đồng bộ policy backend `depreciations.view` (superuser pass qua PermissionHandler).
  const canView = usePermission('depreciations.view');

  interface Depreciation {
    id: string;
    name: string;
    months: number;
  }

  // ST7b — 1 fetch dùng chung cho ProTable (desktop) và ProList (mobile Card).
  const fetchList = async () => {
    const r = await apiClient.get('/depreciations');
    return { list: (r.data.data || []) as Depreciation[], total: (r.data.data || []).length };
  };

  // Trang chỉ xem — không có action buttons (endpoint read-only duy nhất của resource).

  const columns: ProColumns<Depreciation>[] = [
    { title: 'Tên', dataIndex: 'name', key: 'name' },
    { title: 'Số tháng', dataIndex: 'months', key: 'months', width: 120 },
  ];

  // ─── Mobile (ST7b): ProList Card thay Table — cùng fetch ───
  if (isMobile) {
    return (
      <div>
        <ProList<Depreciation>
          headerTitle="Cấu hình khấu hao"
          actionRef={actionRef}
          rowKey="id"
          ghost
          cardProps={false}
          search={false}
          grid={{ gutter: 16, xs: 1, sm: 1 }}
          toolBarRender={() => [
            canView && (
              <Button key="reload" icon={<ReloadOutlined />} onClick={() => actionRef.current?.reload()}>
                Tải lại
              </Button>
            ),
          ]}
          request={async () => {
            try {
              const { list, total } = await fetchList();
              return { data: list, success: true, total };
            } catch {
              message.error('Lỗi tải cấu hình khấu hao');
              return { data: [], success: false, total: 0 };
            }
          }}
          pagination={{ defaultPageSize: 20, showSizeChanger: false }}
          itemRender={(record) => (
            <Card hoverable style={{ borderRadius: 12, marginBottom: 16 }} styles={{ body: { padding: 16 } }}>
              <div style={{ display: 'flex', alignItems: 'center', gap: 10, marginBottom: 10 }}>
                <Text strong style={{ fontSize: 15 }}>{record.name}</Text>
              </div>
              <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: '6px 12px', padding: '10px 12px', background: '#fafafa', borderRadius: 8 }}>
                <Text type="secondary" style={{ fontSize: 12 }}>Số tháng</Text>
                <Text style={{ fontSize: 13 }}>{record.months}</Text>
              </div>
              <Divider style={{ margin: '10px 0' }} />
              <Text type="secondary" style={{ fontSize: 12 }}>Chỉ xem — cấu hình khấu hao dùng chung cho báo cáo và Model.</Text>
            </Card>
          )}
        />
      </div>
    );
  }

  return (
    <div>
      <ProTable<Depreciation>
        headerTitle="Cấu hình khấu hao"
        rowKey="id"
        size="small"
        columns={columns}
        actionRef={actionRef}
        search={false}
        options={{ reload: true, density: true, setting: true }}
        toolBarRender={() => [
          canView && (
            <Button icon={<ReloadOutlined />} onClick={() => actionRef.current?.reload()}>
              Tải lại
            </Button>
          ),
        ]}
        request={async () => {
          try {
            const { list, total } = await fetchList();
            return { data: list, success: true, total };
          } catch {
            message.error('Lỗi tải cấu hình khấu hao');
            return { data: [], success: false, total: 0 };
          }
        }}
        pagination={false}
        scroll={{ x: 'max-content' }}
      />
    </div>
  );
}
