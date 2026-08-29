import { useCallback, useEffect, useMemo, useState } from 'react';
import {
  App, Alert, Button, Card, Col, Descriptions, Form, Input, InputNumber, Modal, Popconfirm,
  Row, Select, Space, Spin, Table, Tag, Typography,
} from 'antd';
import {
  ArrowLeftOutlined, PlusOutlined, EditOutlined, DeleteOutlined, SendOutlined,
  ExperimentOutlined, CarOutlined, ClusterOutlined,
} from '@ant-design/icons';
import { useNavigate, useParams } from 'react-router-dom';
import dayjs from 'dayjs';
import apiClient from '../../../services/api-client';
import { usePermission } from '../../../hooks/usePermission';
import { useIsMobile } from '../../../hooks/useIsMobile';

interface TemplateDetailDto {
  id: string;
  name: string;
  isActive: boolean;
  companyId?: string | null;
  company?: { id: string; name: string } | null;
  systemInfo: {
    id: string; code: string; name: string;
    /** [MC-7d] Vị trí của hệ thống template — nguồn options cho multi-select vị trí áp dụng. */
    positions?: Array<{ id: string; code: string; name: string }>;
  };
  versions: VersionSummaryDto[];
}

interface VersionSummaryDto {
  id: string;
  versionNumber: number;
  effectiveFrom?: string | null;
  publishedAt?: string | null;
  isCurrent: boolean;
  itemsCount: number;
  paramsCount: number;
  campaignCount: number;
}

interface ChecklistItemDto {
  id: string;
  order: number;
  name: string;
  cycleMonths: number;
  toolsRequired?: string | null;
  instruction?: string | null;
  /** [MC-7b] PositionIds [] = universal (mọi vị trí). [MC-7d] hiển thị Tag + form multi-select. */
  positionIds?: string[];
  positionNames?: Array<string | null>;
  /** [MC-8] Tiêu chuẩn kỹ thuật THUỘC VỀ hạng mục này (nested, không còn mảng rời cấp Version). */
  standardParams?: StandardParamDto[];
}

interface StandardParamDto {
  id: string;
  paramName: string;
  nominalValue?: string | null;
  /** [MC-10] Ngưỡng cấu trúc: toán tử (LessThan/LessOrEqual/GreaterThan/GreaterOrEqual/Equal) + giá trị số. */
  thresholdOperator?: string | null;
  thresholdValue?: number | null;
  checkMethod?: string | null;
  unit?: string | null;
}

/** [MC-10] Ký hiệu toán tử ngưỡng cho cột "Ngưỡng" trong bảng tiêu chuẩn. */
const THRESHOLD_OP_SYMBOL: Record<string, string> = {
  LessThan: '<',
  LessOrEqual: '≤',
  GreaterThan: '>',
  GreaterOrEqual: '≥',
  Equal: '=',
};

/** [MC-10] Thể hiện ngưỡng dạng "< 80 %" / "≤ 70 %" / "> 5" — nếu thiếu operator/value → '—'. */
function formatThreshold(p?: StandardParamDto | null): string {
  if (!p || !p.thresholdOperator || p.thresholdValue == null) return '—';
  const sym = THRESHOLD_OP_SYMBOL[p.thresholdOperator] ?? p.thresholdOperator;
  return `${sym} ${p.thresholdValue}${p.unit ? ` ${p.unit}` : ''}`;
}

interface VersionDetailDto {
  id: string;
  versionNumber: number;
  effectiveFrom?: string | null;
  publishedAt?: string | null;
  isCurrent: boolean;
  hasCampaigns: boolean;
  /** Backend cho biết version còn sửa được hay không — frontend KHÔNG tự suy luận (MC-2 API). */
  editable: boolean;
  items: ChecklistItemDto[];
}

const { Text } = Typography;

function fmtDate(v?: string | null): string {
  if (!v) return '—';
  return dayjs(v).format('DD/MM/YYYY HH:mm');
}

/**
 * MC-5 — Template builder: quản lý các TemplateVersion (draft → publish) + ChecklistItem +
 * StandardParam. Trạng thái sửa/khóa lấy thẳng field `editable` từ API MC-2 (KHÔNG tự suy luận).
 */
export default function MaintenanceTemplateBuilderPage() {
  const { message } = App.useApp();
  const navigate = useNavigate();
  const { id } = useParams<{ id: string }>();
  const isMobile = useIsMobile();
  const canManage = usePermission('maintenance.templates');

  const [template, setTemplate] = useState<TemplateDetailDto | null>(null);
  const [version, setVersion] = useState<VersionDetailDto | null>(null);
  const [selectedVersionId, setSelectedVersionId] = useState<string | undefined>(undefined);
  const [loading, setLoading] = useState(true);

  // Item modal
  const [itemOpen, setItemOpen] = useState(false);
  const [itemEditing, setItemEditing] = useState<ChecklistItemDto | null>(null);
  const [itemForm] = Form.useForm();
  // Param modal — [MC-8] tiêu chuẩn thuộc về 1 item cụ thể → giữ itemId đang quản lý.
  const [paramOpen, setParamOpen] = useState(false);
  const [paramEditing, setParamEditing] = useState<StandardParamDto | null>(null);
  const [paramItemId, setParamItemId] = useState<string | null>(null);
  const [paramForm] = Form.useForm();
  // New version modal
  const [newVerOpen, setNewVerOpen] = useState(false);
  const [newVerForm] = Form.useForm();
  const [publishing, setPublishing] = useState(false);
  const [savingItem, setSavingItem] = useState(false);
  const [savingParam, setSavingParam] = useState(false);

  const templateId = id ?? '';

  const loadTemplate = useCallback(async () => {
    try {
      const res = await apiClient.get(`/maintenance/templates/${templateId}`);
      setTemplate(res.data.data);
    } catch {
      setTemplate(null);
    }
  }, [templateId]);

  const loadVersion = useCallback(async (versionId: string) => {
    setLoading(true);
    try {
      const res = await apiClient.get(`/maintenance/templates/${templateId}/versions/${versionId}`);
      setVersion(res.data.data);
    } catch (err: unknown) {
      const e = err as { response?: { data?: { message?: string } } };
      message.error(e?.response?.data?.message || 'Lỗi tải version');
      setVersion(null);
    } finally {
      setLoading(false);
    }
  }, [templateId, message]);

  // Initial load: template detail; then pick current (or latest) version → loadVersion via effect.
  useEffect(() => {
    if (!templateId) return;
    void loadTemplate();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [templateId]);

  useEffect(() => {
    if (!template) return;
    const current = template.versions.find(v => v.isCurrent);
    const first = current ?? template.versions[0];
    if (first) setSelectedVersionId(first.id);
  }, [template]);

  useEffect(() => {
    if (selectedVersionId) void loadVersion(selectedVersionId);
  }, [selectedVersionId, loadVersion]);

  const versionsSorted = useMemo(
    () => template ? [...template.versions].sort((a, b) => b.versionNumber - a.versionNumber) : [],
    [template],
  );

  const versionOptions = versionsSorted.map(v => ({
    label: `v${v.versionNumber}${v.isCurrent ? ' (hiện hành)' : ''}${v.publishedAt ? '' : ' — nháp'}`,
    value: v.id,
  }));

  const isDraft = version ? !version.publishedAt : false;

  // ==================== Items ====================
  const openAddItem = () => {
    setItemEditing(null);
    itemForm.resetFields();
    setItemOpen(true);
  };
  const openEditItem = (it: ChecklistItemDto) => {
    setItemEditing(it);
    itemForm.setFieldsValue(it);
    setItemOpen(true);
  };
  const saveItem = async () => {
    if (!version) return;
    setSavingItem(true);
    try {
      const values = await itemForm.validateFields();
      if (itemEditing) {
        await apiClient.put(`/maintenance/templates/${templateId}/versions/${version.id}/items/${itemEditing.id}`, values);
        message.success('Đã cập nhật hạng mục');
      } else {
        await apiClient.post(`/maintenance/templates/${templateId}/versions/${version.id}/items`, values);
        message.success('Đã thêm hạng mục');
      }
      setItemOpen(false);
      await loadVersion(version.id);
    } catch (err: unknown) {
      const e = err as { errorFields?: unknown; response?: { data?: { message?: string; error_code?: string } } };
      if (e?.errorFields) return;
      message.error(e?.response?.data?.message || 'Lỗi lưu hạng mục');
    } finally {
      setSavingItem(false);
    }
  };
  const deleteItem = async (it: ChecklistItemDto) => {
    if (!version) return;
    try {
      await apiClient.delete(`/maintenance/templates/${templateId}/versions/${version.id}/items/${it.id}`);
      message.success('Đã xóa hạng mục');
      await loadVersion(version.id);
    } catch (err: unknown) {
      const e = err as { response?: { data?: { message?: string } } };
      message.error(e?.response?.data?.message || 'Không thể xóa hạng mục');
    }
  };

  // ==================== Params (MC-8: thuộc về 1 item) ====================
  const openAddParam = (itemId: string) => {
    setParamItemId(itemId);
    setParamEditing(null);
    paramForm.resetFields();
    setParamOpen(true);
  };
  const openEditParam = (itemId: string, p: StandardParamDto) => {
    setParamItemId(itemId);
    setParamEditing(p);
    paramForm.setFieldsValue(p);
    setParamOpen(true);
  };
  const saveParam = async () => {
    if (!version || !paramItemId) return;
    setSavingParam(true);
    try {
      const values = await paramForm.validateFields();
      if (paramEditing) {
        await apiClient.put(`/maintenance/templates/${templateId}/versions/${version.id}/items/${paramItemId}/standard-params/${paramEditing.id}`, values);
        message.success('Đã cập nhật tiêu chuẩn');
      } else {
        await apiClient.post(`/maintenance/templates/${templateId}/versions/${version.id}/items/${paramItemId}/standard-params`, values);
        message.success('Đã thêm tiêu chuẩn');
      }
      setParamOpen(false);
      await loadVersion(version.id);
    } catch (err: unknown) {
      const e = err as { errorFields?: unknown; response?: { data?: { message?: string; error_code?: string } } };
      if (e?.errorFields) return;
      message.error(e?.response?.data?.message || 'Lỗi lưu tiêu chuẩn');
    } finally {
      setSavingParam(false);
    }
  };
  const deleteParam = async (itemId: string, p: StandardParamDto) => {
    if (!version) return;
    try {
      await apiClient.delete(`/maintenance/templates/${templateId}/versions/${version.id}/items/${itemId}/standard-params/${p.id}`);
      message.success('Đã xóa tiêu chuẩn');
      await loadVersion(version.id);
    } catch (err: unknown) {
      const e = err as { response?: { data?: { message?: string } } };
      message.error(e?.response?.data?.message || 'Không thể xóa tiêu chuẩn');
    }
  };

  // ==================== Versions ====================
  const createVersion = async () => {
    try {
      const values = await newVerForm.validateFields();
      const payload: Record<string, unknown> = {};
      // datetime-local → chuỗi local "YYYY-MM-DDTHH:mm" → parse theo múi giờ máy → UTC ISO.
      if (values.effectiveFrom) payload.effectiveFrom = new Date(values.effectiveFrom).toISOString();
      const res = await apiClient.post(`/maintenance/templates/${templateId}/versions`, payload);
      message.success('Đã tạo version nháp mới');
      setNewVerOpen(false);
      newVerForm.resetFields();
      await loadTemplate();
      setSelectedVersionId(res.data.data.id);
    } catch (err: unknown) {
      const e = err as { errorFields?: unknown; response?: { data?: { message?: string } } };
      if (e?.errorFields) return;
      message.error(e?.response?.data?.message || 'Lỗi tạo version');
    }
  };

  const publishVersion = async () => {
    if (!version) return;
    setPublishing(true);
    try {
      await apiClient.post(`/maintenance/templates/${templateId}/versions/${version.id}/publish`);
      message.success(`Đã publish version ${version.versionNumber}`);
      await loadTemplate();
      await loadVersion(version.id);
    } catch (err: unknown) {
      const e = err as { response?: { data?: { message?: string } } };
      message.error(e?.response?.data?.message || 'Lỗi publish');
    } finally {
      setPublishing(false);
    }
  };

  // ==================== Render ====================
  if (loading && !version) {
    return <div style={{ textAlign: 'center', padding: 64 }}><Spin size="large" /></div>;
  }

  if (!template) {
    return (
      <div>
        <Alert type="warning" showIcon title="Template không tồn tại hoặc ngoài phạm vi công ty của bạn." />
        <Button icon={<ArrowLeftOutlined />} style={{ marginTop: 16 }} onClick={() => navigate('/maintenance/templates')}>
          Quay lại danh sách
        </Button>
      </div>
    );
  }

  const editable = version?.editable ?? false;

  return (
    <div>
      <Space style={{ marginBottom: 12 }}>
        <Button icon={<ArrowLeftOutlined />} onClick={() => navigate('/maintenance/templates')} />
        <Text strong style={{ fontSize: 16 }}>{template.name}</Text>
        <Tag icon={<ClusterOutlined />}>{template.systemInfo.name} ({template.systemInfo.code})</Tag>
        {template.company ? <Tag>{template.company.name}</Tag> : <Tag>Floater</Tag>}
        {!template.isActive && <Tag color="orange">Ngừng kích hoạt</Tag>}
      </Space>

      <Card size="small" style={{ marginBottom: 16 }}>
        <Row gutter={16} align="middle">
          <Col xs={24} sm={12} md={8}>
            <Space>
              <span>Phiên bản:</span>
              <Select
                style={{ minWidth: 220 }}
                value={selectedVersionId}
                onChange={setSelectedVersionId}
                options={versionOptions}
              />
            </Space>
          </Col>
          <Col xs={24} sm={12} md={16}>
            <Space wrap>
              {canManage && version && isDraft && (
                <Popconfirm
                  title="Publish version này?"
                  description="Sau khi publish, version trở thành bản hiện hành. Nếu đã có đợt bảo dưỡng (Campaign) dùng version này, nội dung sẽ KHÔNG THỂ SỬA ĐƯỢC NỮA — mọi thay đổi phải tạo version mới."
                  okText="Publish"
                  okButtonProps={{ danger: true }}
                  onConfirm={publishVersion}
                >
                  <Button type="primary" icon={<SendOutlined />} loading={publishing}>
                    Publish version
                  </Button>
                </Popconfirm>
              )}
              {canManage && (
                <Button icon={<PlusOutlined />} onClick={() => setNewVerOpen(true)}>
                  Tạo version mới
                </Button>
              )}
              {version && (
                <Tag color={version.isCurrent ? 'blue' : 'default'}>
                  {version.isCurrent ? 'Hiện hành' : 'Không hiện hành'}
                </Tag>
              )}
            </Space>
          </Col>
        </Row>
        {version && (
          <Descriptions size="small" column={isMobile ? 1 : 4} style={{ marginTop: 8 }}>
            <Descriptions.Item label="Version">v{version.versionNumber}</Descriptions.Item>
            <Descriptions.Item label="Publish lúc">{fmtDate(version.publishedAt)}</Descriptions.Item>
            <Descriptions.Item label="Hiệu lực từ">{fmtDate(version.effectiveFrom)}</Descriptions.Item>
            <Descriptions.Item label="Đợt bảo dưỡng dùng">
              {version.hasCampaigns ? <Tag color="green">Có</Tag> : <Tag>Chưa</Tag>}
            </Descriptions.Item>
          </Descriptions>
        )}
        {version && !editable && (
          <Alert
            style={{ marginTop: 12 }}
            type="warning"
            showIcon
            title={version.hasCampaigns
              ? `Version này đã có đợt bảo dưỡng sử dụng — nội dung bất biến. Muốn thay đổi checklist, hãy tạo version mới.`
              : 'Version đã publish — nội dung bị khóa.'}
          />
        )}
      </Card>

      {/* Checklist items — [MC-8] tiêu chuẩn kỹ thuật lồng bên trong từng hạng mục (expandable row) */}
      <Card
        size="small"
        title={<Space><ExperimentOutlined /> ChecklistItems (danh sách kiểm tra)</Space>}
        extra={canManage && editable && version && (
          <Button size="small" type="primary" icon={<PlusOutlined />} onClick={openAddItem}>Thêm hạng mục</Button>
        )}
      >
        <Table<ChecklistItemDto>
          rowKey="id"
          size="small"
          dataSource={version?.items ?? []}
          pagination={false}
          scroll={{ x: 'max-content' }}
          expandable={{
            rowExpandable: () => true,
            expandedRowRender: (it) => (
              <div style={{ padding: '4px 8px' }}>
                <Space orientation="vertical" style={{ width: '100%' }}>
                  <Space style={{ justifyContent: 'space-between', width: '100%' }}>
                    <Text strong style={{ fontSize: 13 }}>Tiêu chuẩn kỹ thuật của hạng mục "{it.name}"</Text>
                    {canManage && editable && (
                      <Button size="small" type="primary" icon={<PlusOutlined />} onClick={() => openAddParam(it.id)}>
                        Thêm tiêu chuẩn
                      </Button>
                    )}
                  </Space>
                  <Table<StandardParamDto>
                    rowKey="id"
                    size="small"
                    dataSource={it.standardParams ?? []}
                    pagination={false}
                    locale={{ emptyText: 'Chưa có tiêu chuẩn kỹ thuật nào.' }}
                    scroll={{ x: 'max-content' }}
                    columns={[
                      { title: 'Thông số', dataIndex: 'paramName', key: 'paramName', width: 140 },
                      { title: 'Định mức', dataIndex: 'nominalValue', key: 'nominalValue', width: 100, render: (v?: string | null) => v || '—' },
                      { title: 'Ngưỡng', dataIndex: 'thresholdValue', key: 'thresholdValue', width: 100, render: (_, p: StandardParamDto) => <span>{formatThreshold(p)}</span> },
                      { title: 'Cách kiểm tra', dataIndex: 'checkMethod', key: 'checkMethod', width: 130, render: (v?: string | null) => v || '—' },
                      { title: 'Đơn vị', dataIndex: 'unit', key: 'unit', width: 70, render: (v?: string | null) => v || '—' },
                      ...(editable ? [{
                        title: 'Hành động', key: 'actions', width: 110,
                        render: (_: unknown, p: StandardParamDto) => (
                          <Space size={4}>
                            <Button size="small" icon={<EditOutlined />} onClick={() => openEditParam(it.id, p)} />
                            <Popconfirm title="Xóa tiêu chuẩn?" onConfirm={() => deleteParam(it.id, p)}>
                              <Button size="small" danger icon={<DeleteOutlined />} />
                            </Popconfirm>
                          </Space>
                        ),
                      }] : []),
                    ]}
                  />
                </Space>
              </div>
            ),
          }}
          columns={[
            { title: '#', dataIndex: 'order', key: 'order', width: 56 },
            { title: 'Hạng mục', dataIndex: 'name', key: 'name', width: 200 },
            {
              title: 'Chu kỳ (tháng)', dataIndex: 'cycleMonths', key: 'cycleMonths', width: 110,
              render: (v: number) => <Tag color={v <= 3 ? 'orange' : 'blue'}>{v} tháng</Tag>,
            },
            {
              title: 'Dụng cụ', dataIndex: 'toolsRequired', key: 'toolsRequired', width: 140,
              render: (v?: string | null) => v || '—',
            },
            {
              title: 'Hướng dẫn', dataIndex: 'instruction', key: 'instruction', width: 200,
              render: (v?: string | null) => v || '—',
            },
            {
              // [MC-7d] Phạm vi vị trí: [] = universal → Tag "Mọi vị trí"; có khai báo → Tag từng vị trí.
              title: 'Vị trí áp dụng', key: 'positions', width: 180,
              render: (_: unknown, it: ChecklistItemDto) => {
                const names = it.positionNames ?? [];
                if (names.length === 0) return <Tag icon={<ClusterOutlined />}>Mọi vị trí</Tag>;
                return (
                  <Space size={4} wrap>
                    {names.map((n, idx) => (
                      <Tag key={idx} color="blue">{n || '—'}</Tag>
                    ))}
                  </Space>
                );
              },
            },
            {
              // [MC-8] Số tiêu chuẩn của riêng hạng mục — nhất quán với cách hiển thị Positions.
              title: 'Tiêu chuẩn', key: 'params', width: 100,
              render: (_: unknown, it: ChecklistItemDto) => {
                const n = it.standardParams?.length ?? 0;
                return <Tag icon={<CarOutlined />} color={n > 0 ? 'green' : 'default'}>{n} tiêu chuẩn</Tag>;
              },
            },
            ...(editable ? [{
              title: 'Hành động', key: 'actions', width: 110,
              render: (_: unknown, it: ChecklistItemDto) => (
                <Space size={4}>
                  <Button size="small" icon={<EditOutlined />} onClick={() => openEditItem(it)} />
                  <Popconfirm title="Xóa hạng mục?" onConfirm={() => deleteItem(it)}>
                    <Button size="small" danger icon={<DeleteOutlined />} />
                  </Popconfirm>
                </Space>
              ),
            }] : []),
          ]}
        />
      </Card>

      {/* Item modal */}
      <Modal
        open={itemOpen}
        title={itemEditing ? 'Sửa hạng mục' : 'Thêm hạng mục'}
        onOk={itemForm.submit}
        onCancel={() => setItemOpen(false)}
        confirmLoading={savingItem}
        destroyOnHidden
        width={isMobile ? '95%' : 560}
      >
        <Form form={itemForm} layout="vertical" onFinish={saveItem}>
          <Form.Item label="Tên hạng mục" name="name" rules={[{ required: true, message: 'Vui lòng nhập tên' }]}>
            <Input maxLength={255} placeholder="VD: Vệ sinh quạt tản nhiệt" />
          </Form.Item>
          <Form.Item
            label="Chu kỳ (tháng)"
            name="cycleMonths"
            rules={[{ required: true, message: 'Vui lòng nhập chu kỳ' }]}
            initialValue={12}
          >
            <InputNumber min={1} max={120} style={{ width: '100%' }} placeholder="Số tháng giữa 2 lần bảo dưỡng" />
          </Form.Item>
          <Form.Item label="Thứ tự" name="order" tooltip="Bỏ trống = tự tăng sau hạng mục cuối">
            <InputNumber min={1} style={{ width: '100%' }} placeholder="Tự động nếu bỏ trống" />
          </Form.Item>
          <Form.Item label="Dụng cụ cần thiết" name="toolsRequired">
            <Input.TextArea rows={2} placeholder="VD: tua vít, khí nén" />
          </Form.Item>
          <Form.Item label="Hướng dẫn thực hiện" name="instruction">
            <Input.TextArea rows={3} placeholder="Mô tả các bước kiểm tra/bảo dưỡng" />
          </Form.Item>
          <Form.Item
            // [MC-7d] Vị trí áp dụng: multi-select từ vị trí của hệ thống template.
            // Bỏ trống (hoặc chọn rồi xóa hết) = universal — hạng mục áp dụng cho MỌI vị trí.
            label="Vị trí áp dụng"
            name="positionIds"
            tooltip="Chọn các vị trí hệ thống mà hạng mục này áp dụng. Bỏ trống = mọi vị trí."
            extra={<Text type="secondary">Bỏ trống = áp dụng cho mọi vị trí (universal).</Text>}
          >
            <Select
              mode="multiple"
              allowClear
              placeholder="Bỏ trống = mọi vị trí"
              options={(template.systemInfo.positions ?? []).map(p => ({ label: `${p.name} (${p.code})`, value: p.id }))}
              optionFilterProp="label"
              maxTagCount="responsive"
            />
          </Form.Item>
        </Form>
      </Modal>

      {/* Param modal */}
      <Modal
        open={paramOpen}
        title={paramEditing ? 'Sửa tiêu chuẩn' : 'Thêm tiêu chuẩn kỹ thuật'}
        onOk={paramForm.submit}
        onCancel={() => setParamOpen(false)}
        confirmLoading={savingParam}
        destroyOnHidden
        width={isMobile ? '95%' : 560}
      >
        <Form form={paramForm} layout="vertical" onFinish={saveParam}>
          <Form.Item label="Tên thông số" name="paramName" rules={[{ required: true, message: 'Vui lòng nhập' }]}>
            <Input maxLength={100} placeholder="VD: CPU load" />
          </Form.Item>
          <Row gutter={12}>
            <Col span={12}>
              <Form.Item label="Giá trị định mức" name="nominalValue">
                <Input placeholder="VD: 60%" />
              </Form.Item>
            </Col>
            <Col span={12}>
              <Form.Item label="Đơn vị" name="unit">
                <Input placeholder="VD: %" maxLength={20} />
              </Form.Item>
            </Col>
          </Row>
          <Row gutter={12}>
            <Col span={10}>
              <Form.Item
                // [MC-10] Ngưỡng BẮT BUỘC cấu trúc: chọn toán tử so sánh (thay ô text tự do "VD: <60%").
                label="Toán tử so sánh"
                name="thresholdOperator"
                rules={[{ required: true, message: 'Chọn toán tử' }]}
                tooltip="Cách so sánh giá trị đo với ngưỡng để tự suy Đạt/Không đạt"
              >
                <Select
                  options={[
                    { label: '< (nhỏ hơn)', value: 'LessThan' },
                    { label: '≤ (nhỏ hơn hoặc bằng)', value: 'LessOrEqual' },
                    { label: '> (lớn hơn)', value: 'GreaterThan' },
                    { label: '≥ (lớn hơn hoặc bằng)', value: 'GreaterOrEqual' },
                    { label: '= (bằng)', value: 'Equal' },
                  ]}
                />
              </Form.Item>
            </Col>
            <Col span={14}>
              <Form.Item
                label="Giá trị ngưỡng"
                name="thresholdValue"
                rules={[{ required: true, message: 'Nhập số' }]}
              >
                <InputNumber min={0} style={{ width: '100%' }} placeholder="VD: 80" />
              </Form.Item>
            </Col>
          </Row>
          <Form.Item label="Cách kiểm tra" name="checkMethod">
            <Input placeholder="VD: ipmitool sensor" />
          </Form.Item>
        </Form>
      </Modal>

      {/* New version modal */}
      <Modal
        open={newVerOpen}
        title="Tạo version mới (nháp)"
        onOk={newVerForm.submit}
        onCancel={() => setNewVerOpen(false)}
        destroyOnHidden
      >
        <Form form={newVerForm} layout="vertical" onFinish={createVersion}>
          <Form.Item label="Hiệu lực từ" name="effectiveFrom" tooltip="Bỏ trống = lấy thời điểm publish">
            <Input type="datetime-local" />
          </Form.Item>
        </Form>
      </Modal>
    </div>
  );
}
