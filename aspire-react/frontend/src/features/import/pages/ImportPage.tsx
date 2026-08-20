import { useState } from 'react';
import {
  Alert, App, Button, Card, Empty, Radio, Segmented, Space, Table, Tag, Typography, Upload,
} from 'antd';
import type { UploadFile } from 'antd';
import {
  CloudUploadOutlined, DownloadOutlined, ImportOutlined,
} from '@ant-design/icons';
import CompanyTreeSelect from '../../../components/common/CompanyTreeSelect';
import { usePermissionMap } from '../../../hooks/usePermission';
import {
  downloadImportTemplate, importExcel, type ImportResult, type ImportType,
} from '../services/import.service';

const { Title, Text, Paragraph } = Typography;
const { Dragger } = Upload;

interface ImportTypeOption {
  value: ImportType;
  label: string;
  /** Permission code required for this entity type (mirrors backend [Authorize(Policy)]). */
  permCode: string;
  hint: string;
}

const IMPORT_TYPES: ImportTypeOption[] = [
  { value: 'reference', label: 'Danh mục / Địa điểm / Nhà sản xuất', permCode: 'categories.create', hint: 'Sheet 1_DanhMuc, 2_DiaDiem, 3_NhaSanXuat — thiếu sẽ tự tạo. Danh mục & Nhà SX là dữ liệu toàn cục; công ty chọn áp cho Địa điểm và ghi vào nhật ký.' },
  { value: 'assets', label: 'Tài sản', permCode: 'assets.create', hint: 'Sheet 4_TaiSan — Model KHÔNG tự tạo (báo lỗi từng dòng nếu thiếu).' },
  { value: 'components', label: 'Linh kiện', permCode: 'components.create', hint: 'Sheet 5_LinhKien — các dòng serial cùng (Tên + Danh mục + Model) gom thành 1 linh kiện.' },
  { value: 'accessories', label: 'Phụ kiện', permCode: 'accessories.create', hint: 'Sheet 6_PhuKien — danh mục / nhà SX / địa điểm phải tồn tại trước.' },
  { value: 'consumables', label: 'Vật tư tiêu hao', permCode: 'consumables.create', hint: 'Sheet 7_VatTuTieuHao — danh mục / nhà SX / địa điểm phải tồn tại trước.' },
  { value: 'systems', label: 'Hệ thống', permCode: 'systems.create', hint: 'Sheet 1_HeThong — mã tự sinh SYS-<năm>-<STT>. Import hệ thống TRƯỚC rồi mới import vị trí.' },
  { value: 'system-positions', label: 'Vị trí trong hệ thống', permCode: 'systems.create', hint: 'Sheet 2_ViTri — cột "Hệ thống cha (tên)" phải khớp SystemInfo đã import trước; vị trí kế thừa công ty từ hệ thống cha.' },
];

export default function ImportPage() {
  const { message } = App.useApp();
  const perm = usePermissionMap();

  const [importType, setImportType] = useState<ImportType>('assets');
  const [companyId, setCompanyId] = useState<string | undefined>(undefined);
  const [fileList, setFileList] = useState<UploadFile[]>([]);
  const [loading, setLoading] = useState(false);
  const [downloadingTpl, setDownloadingTpl] = useState(false);
  const [result, setResult] = useState<ImportResult | null>(null);
  const [rowFilter, setRowFilter] = useState<'all' | 'ok' | 'error'>('all');

  const hasPerm = (code: string): boolean => {
    if (!perm) return false; // still loading → fail-closed for the options list
    if (perm.isSuperUser) return true;
    return (perm.permissions[code] ?? 0) === 1;
  };

  const allowedTypes = IMPORT_TYPES.filter(t => hasPerm(t.permCode));
  const selectedType = IMPORT_TYPES.find(t => t.value === importType);
  const file = (fileList[0]?.originFileObj ?? undefined) as File | undefined;

  // Client-side pre-validation (backend re-validates everything — never trusts the client).
  const missing = !importType ? 'Chọn loại dữ liệu.' : !companyId ? 'Chọn công ty áp dụng cho lần import này.' : !file ? 'Chọn file Excel (.xlsx).' : null;

  const handleImport = async () => {
    if (missing || !selectedType || !file || !companyId) return;
    setLoading(true);
    setResult(null);
    try {
      const res = await importExcel(importType, file, companyId);
      setResult(res);
      if (res.failed === 0) {
        message.success(`Import hoàn tất: ${res.created} bản ghi.`);
      } else {
        message.warning(`Import hoàn tất: ${res.created} thành công, ${res.failed} lỗi — xem báo cáo bên dưới.`);
      }
    } catch (err: unknown) {
      const e = err as { response?: { status?: number; data?: { message?: string; error_code?: string } } };
      const msg = e?.response?.data?.message;
      // HTTP status check (403 = out-of-scope company) — the sweep exempts `response?.status` numeric checks.
      if (e?.response?.status === 403) {
        message.error(msg ?? 'Công ty đã chọn nằm ngoài phạm vi quyền của bạn.');
      } else {
        message.error(msg ?? 'Import thất bại.');
      }
    } finally {
      setLoading(false);
    }
  };

  const handleTemplate = async () => {
    setDownloadingTpl(true);
    try {
      await downloadImportTemplate(importType);
    } catch {
      message.error('Không tải được file mẫu.');
    } finally {
      setDownloadingTpl(false);
    }
  };

  if (!perm) return null; // permissions still loading

  if (allowedTypes.length === 0) {
    return (
      <Empty description="Bạn không có quyền tạo dữ liệu nào (categories/assets/components/accessories/consumables) nên không thể import." />
    );
  }

  const filteredRows = (result?.rows ?? []).filter(r =>
    rowFilter === 'all' ? true : rowFilter === 'ok' ? r.success : !r.success,
  );

  return (
    <div>
      <Title level={4} style={{ marginTop: 0 }}>Import Excel</Title>
      <Paragraph type="secondary">
        Import dữ liệu từ file Excel (.xlsx) theo cấu trúc file mẫu. Mỗi lần import áp dụng cho
        ĐÚNG MỘT công ty được chọn bên dưới. Import chạy ngay (best-effort từng dòng) — dòng lỗi
        được báo cáo chi tiết, không chặn các dòng khác.
      </Paragraph>

      <Card style={{ marginBottom: 16 }}>
        <Space direction="vertical" size="large" style={{ width: '100%' }}>
          <div>
            <Text strong>1. Loại dữ liệu</Text>
            <Radio.Group
              style={{ display: 'flex', flexDirection: 'column', gap: 8, marginTop: 8 }}
              value={importType}
              onChange={(e) => { setImportType(e.target.value as ImportType); setResult(null); }}
              options={IMPORT_TYPES.map(t => ({
                value: t.value,
                label: t.label,
                disabled: !hasPerm(t.permCode),
              }))}
            />
            {selectedType && <Text type="secondary" style={{ display: 'block', marginTop: 4 }}>{selectedType.hint}</Text>}
          </div>

          <div>
            <Text strong>2. Công ty áp dụng <Text type="danger">*(bắt buộc)</Text></Text>
            <div style={{ maxWidth: 480, marginTop: 8 }}>
              <CompanyTreeSelect
                value={companyId}
                onChange={(v) => { setCompanyId(v); setResult(null); }}
                placeholder="Chọn công ty cho lần import này"
              />
            </div>
            <Text type="secondary" style={{ display: 'block', marginTop: 4 }}>
              Chỉ hiện các công ty trong phạm vi quyền của bạn. Toàn bộ bản ghi tạo ra từ file này
              sẽ thuộc công ty đã chọn.
            </Text>
          </div>

          <div>
            <Text strong>3. File Excel (.xlsx)</Text>
            <div style={{ marginTop: 8 }}>
              <Dragger
                accept=".xlsx"
                maxCount={1}
                fileList={fileList}
                beforeUpload={(f) => {
                  const isXlsx = f.name.toLowerCase().endsWith('.xlsx');
                  if (!isXlsx) {
                    message.error('Chỉ hỗ trợ file .xlsx.');
                    return Upload.LIST_IGNORE;
                  }
                  return false; // manual upload — gửi qua nút Import
                }}
                onChange={(info) => { setFileList(info.fileList.slice(-1)); setResult(null); }}
              >
                <p className="ant-upload-drag-icon"><CloudUploadOutlined /></p>
                <p className="ant-upload-text">Kéo thả hoặc bấm để chọn file .xlsx</p>
              </Dragger>
            </div>
          </div>

          <Space wrap>
            <Button
              type="primary"
              icon={<ImportOutlined />}
              loading={loading}
              disabled={!!missing}
              onClick={() => void handleImport()}
            >
              Import
            </Button>
            <Button
              icon={<DownloadOutlined />}
              loading={downloadingTpl}
              onClick={() => void handleTemplate()}
            >
              Tải file mẫu
            </Button>
          </Space>
        </Space>
      </Card>

      {result && (
        <Card
          title="Báo cáo kết quả import"
          extra={
            <Segmented
              value={rowFilter}
              onChange={(v) => setRowFilter(v as 'all' | 'ok' | 'error')}
              options={[
                { value: 'all', label: `Tất cả (${(result.rows ?? []).length})` },
                { value: 'ok', label: `Thành công (${result.created})` },
                { value: 'error', label: `Lỗi (${result.failed})` },
              ]}
            />
          }
        >
          <Alert
            style={{ marginBottom: 12 }}
            type={result.failed === 0 ? 'success' : result.created === 0 ? 'error' : 'warning'}
            showIcon
            message={`Đã tạo ${result.created} bản ghi — ${result.failed} dòng lỗi.`}
          />
          <Table
            size="small"
            rowKey={(r) => `${r.rowNumber}-${r.message}`}
            dataSource={filteredRows}
            pagination={{ pageSize: 20, showSizeChanger: true }}
            scroll={{ x: 'max-content' }}
            columns={[
              { title: 'Dòng', dataIndex: 'rowNumber', width: 70 },
              {
                title: 'Kết quả', dataIndex: 'success', width: 120,
                render: (ok: boolean) => ok
                  ? <Tag color="success">✓ Thành công</Tag>
                  : <Tag color="error">✗ Lỗi</Tag>,
              },
              { title: 'Chi tiết', dataIndex: 'message' },
            ]}
          />
        </Card>
      )}
    </div>
  );
}
