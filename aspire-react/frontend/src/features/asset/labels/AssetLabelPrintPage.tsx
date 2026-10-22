import { useEffect, useMemo, useState } from 'react';
import { useLocation, useNavigate, useSearchParams } from 'react-router-dom';
import {
  Alert, App, Button, Card, Empty, InputNumber, Select, Space, Spin, Switch, Tag, Typography,
} from 'antd';
import { ArrowLeftOutlined, PrinterOutlined, SettingOutlined } from '@ant-design/icons';
import { QRCode } from 'antd';
import type { AssetDetailDto } from '../types/asset';
import { assetService } from '../services/asset.service';
import { systemsService, type SystemInfoDetailDto } from '../../system/services/systems.service';
import { assetShortUrl, systemPositionUrl } from '../../../utils/publicUrl';
import {
  getLabelTemplate, LABEL_TEMPLATES, LABEL_TEMPLATE_ORDER, templateCapacity,
  type LabelTemplate, type LabelTemplateId,
} from './labelTemplates';
import './AssetLabelPrintPage.css';

const { Title, Text } = Typography;

const SETTINGS_KEY = 'mirats.assetLabelSettings.v1';

interface LabelSettings {
  templateId: LabelTemplateId;
  startCell: number;
  offsetX: number;
  offsetY: number;
  cutLines: boolean;
}

interface LabelItem {
  id: string;
  code: string;
  name: string;
  org: string;
  context: string;
  qrUrl: string;
}

function loadSettings(): LabelSettings {
  const fallback: LabelSettings = {
    templateId: 'standard', startCell: 1, offsetX: 0, offsetY: 0, cutLines: false,
  };
  try {
    const raw = localStorage.getItem(SETTINGS_KEY);
    if (!raw) return fallback;
    const parsed = JSON.parse(raw) as Partial<LabelSettings>;
    return {
      templateId: parsed.templateId && parsed.templateId in LABEL_TEMPLATES ? parsed.templateId : 'standard',
      startCell: Number(parsed.startCell) > 0 ? Number(parsed.startCell) : 1,
      offsetX: Number.isFinite(Number(parsed.offsetX)) ? Number(parsed.offsetX) : 0,
      offsetY: Number.isFinite(Number(parsed.offsetY)) ? Number(parsed.offsetY) : 0,
      cutLines: !!parsed.cutLines,
    };
  } catch {
    return fallback;
  }
}

function labelVars(t: LabelTemplate): React.CSSProperties {
  return {
    '--qr-mm': `${t.qrMm}mm`,
    '--code-pt': `${t.codePt}pt`,
    '--name-pt': `${t.namePt}pt`,
    '--org-pt': `${t.orgPt}pt`,
  } as React.CSSProperties;
}

function LabelFace({ item, template }: { item: LabelItem; template: LabelTemplate }) {
  return (
    <div
      className={`asset-label asset-label--${template.id}`}
      style={{ ...labelVars(template), width: `${template.widthMm}mm`, height: `${template.heightMm}mm` }}
    >
      <div className="asset-label__qr" style={{ padding: `${template.qrPadMm}mm` }}>
        <QRCode
          type="svg"
          value={item.qrUrl}
          size={Math.max(64, Math.round(template.qrMm * 96 / 25.4))}
          errorLevel="Q"
          boostLevel={false}
          marginSize={2}
          bordered={false}
          bgColor="#ffffff"
          color="#000000"
        />
      </div>
      <div className="asset-label__text">
        <div className="asset-label__code" title={item.code}>{item.code}</div>
        {template.showName && <div className="asset-label__name">{item.name}</div>}
        {template.showOrg && item.org && <div className="asset-label__org">{item.org}</div>}
        {template.showContext && item.context && <div className="asset-label__context">{item.context}</div>}
      </div>
    </div>
  );
}

export default function AssetLabelPrintPage() {
  const { message } = App.useApp();
  const navigate = useNavigate();
  const location = useLocation();
  const [searchParams] = useSearchParams();
  const isCalibration = location.pathname.endsWith('/calibration');

  const stored = loadSettings();
  const queryTemplate = searchParams.get('template');
  const [templateId, setTemplateId] = useState<LabelTemplateId>(
    queryTemplate && queryTemplate in LABEL_TEMPLATES ? (queryTemplate as LabelTemplateId) : stored.templateId,
  );
  const [startCell, setStartCell] = useState(stored.startCell);
  const [offsetX, setOffsetX] = useState(stored.offsetX);
  const [offsetY, setOffsetY] = useState(stored.offsetY);
  const [cutLines, setCutLines] = useState(stored.cutLines);
  const [items, setItems] = useState<LabelItem[]>([]);
  const [loading, setLoading] = useState(false);

  const template = getLabelTemplate(templateId);
  const capacity = templateCapacity(template);
  const safeStart = Math.min(Math.max(1, startCell || 1), capacity);
  const idsParam = searchParams.get('ids') ?? '';
  const systemId = searchParams.get('systemId');
  const positionId = searchParams.get('positionId');

  useEffect(() => {
    const settings: LabelSettings = { templateId, startCell: safeStart, offsetX, offsetY, cutLines };
    localStorage.setItem(SETTINGS_KEY, JSON.stringify(settings));
  }, [templateId, safeStart, offsetX, offsetY, cutLines]);

  useEffect(() => {
    if (isCalibration) {
      setItems([]);
      setLoading(false);
      return;
    }

    let cancelled = false;
    const load = async () => {
      setLoading(true);
      try {
        if (systemId && positionId) {
          const res = await systemsService.get(systemId);
          const system = res.data?.data as SystemInfoDetailDto | undefined;
          const position = system?.positions?.find((p) => p.id === positionId);
          if (!position) throw new Error('POSITION_NOT_FOUND');
          if (!cancelled) {
            setItems([{
              id: position.id,
              code: position.code,
              name: position.name,
              org: system?.name ?? '',
              context: `${system?.name ?? ''} / ${position.name}`,
              qrUrl: systemPositionUrl(systemId, positionId),
            }]);
          }
          return;
        }

        const ids = [...new Set(idsParam.split(',').map((s) => s.trim()).filter(Boolean))];
        if (ids.length === 0) {
          if (!cancelled) setItems([]);
          return;
        }

        const assets = await Promise.all(ids.map(async (id) => {
          const res = await assetService.get(id);
          return res.data?.data as AssetDetailDto;
        }));
        if (!cancelled) {
          setItems(assets.filter(Boolean).map((asset) => ({
            id: asset.id,
            code: asset.assetTag,
            name: asset.name,
            org: asset.company?.name ?? '',
            context: '',
            qrUrl: assetShortUrl(asset.id),
          })));
        }
      } catch {
        if (!cancelled) {
          setItems([]);
          void message.error('Không tải được dữ liệu tem. Kiểm tra lại tài sản/vị trí đã chọn.');
        }
      } finally {
        if (!cancelled) setLoading(false);
      }
    };

    void load();
    return () => { cancelled = true; };
  }, [idsParam, systemId, positionId, isCalibration, message]);

  const cells = useMemo<(LabelItem | null)[]>(() => {
    if (isCalibration) return Array.from({ length: capacity }, () => null);
    return [...Array.from({ length: safeStart - 1 }, () => null), ...items];
  }, [isCalibration, capacity, safeStart, items]);

  const gridStyle: React.CSSProperties = {
    gridTemplateColumns: `repeat(${template.cols}, ${template.widthMm}mm)`,
    gridAutoRows: `${template.heightMm}mm`,
    padding: `${template.padY}mm ${template.padX}mm`,
    transform: `translate(${offsetX}mm, ${offsetY}mm)`,
  };

  const calibrationParams = new URLSearchParams(searchParams);
  calibrationParams.set('template', templateId);
  const calibrationHref = `/assets/labels/calibration?${calibrationParams.toString()}`;
  const printHref = `/assets/labels/print?${calibrationParams.toString()}`;

  return (
    <div className="label-print-shell">
      <div className="label-print-toolbar no-print">
        <Space style={{ width: '100%', justifyContent: 'space-between' }} wrap>
          <Space wrap>
            <Button icon={<ArrowLeftOutlined />} onClick={() => navigate(systemId ? `/systems/${systemId}` : '/assets')}>
              Quay lại
            </Button>
            <Title level={4} style={{ margin: 0 }}>
              {isCalibration ? 'In thử căn lề' : 'In tem tài sản'}
            </Title>
            <Tag color="blue">A4 · decal</Tag>
          </Space>
          <Space wrap>
            {isCalibration
              ? <Button onClick={() => navigate(printHref)}>Quay lại in tem</Button>
              : <Button icon={<SettingOutlined />} onClick={() => navigate(calibrationHref)}>In thử căn lề</Button>}
            <Button type="primary" icon={<PrinterOutlined />} onClick={() => window.print()}>
              In tem
            </Button>
          </Space>
        </Space>

        <Card size="small" style={{ marginTop: 12 }}>
          <Space wrap size="middle">
            <span>
              <Text strong>Mẫu tem</Text>
              <Select
                style={{ width: 210, marginLeft: 8 }}
                value={templateId}
                onChange={(v: LabelTemplateId) => { setTemplateId(v); setStartCell((n) => Math.min(n, templateCapacity(LABEL_TEMPLATES[v]))); }}
                options={LABEL_TEMPLATE_ORDER.map((id) => ({
                  value: id,
                  label: `${LABEL_TEMPLATES[id].name} · ${LABEL_TEMPLATES[id].sizeLabel} · ${LABEL_TEMPLATES[id].cols}×${LABEL_TEMPLATES[id].rows}`,
                }))}
              />
            </span>
            {!isCalibration && (
              <span>
                <Text strong>Bắt đầu từ ô số</Text>
                <InputNumber
                  min={1}
                  max={capacity}
                  value={safeStart}
                  onChange={(v) => setStartCell(Number(v) || 1)}
                  style={{ width: 90, marginLeft: 8 }}
                />
              </span>
            )}
            <span>
              <Text strong>Lệch X</Text>
              <InputNumber
                min={-30}
                max={30}
                step={0.5}
                value={offsetX}
                onChange={(v) => setOffsetX(Number(v) || 0)}
                suffix="mm"
                style={{ width: 120, marginLeft: 8 }}
              />
            </span>
            <span>
              <Text strong>Lệch Y</Text>
              <InputNumber
                min={-30}
                max={30}
                step={0.5}
                value={offsetY}
                onChange={(v) => setOffsetY(Number(v) || 0)}
                suffix="mm"
                style={{ width: 120, marginLeft: 8 }}
              />
            </span>
            <span>
              <Switch size="small" checked={cutLines} onChange={setCutLines} />
              <Text style={{ marginLeft: 6 }}>Đường cắt mảnh</Text>
            </span>
          </Space>
          <div style={{ marginTop: 10 }}>
            <Alert
              type="info"
              showIcon
              title="Khi in: chọn khổ A4, tỉ lệ 100%, TẮT “Vừa trang” / “Fit to page”. Kiểm tra bằng “In thử căn lề” trước khi in decal."
              description="Lệch X/Y và mẫu tem được lưu trên máy này. Mỗi ô tem đúng kích thước mm khi in 100%."
            />
          </div>
        </Card>
      </div>

      {loading ? (
        <Spin style={{ display: 'block', margin: '80px auto' }} />
      ) : !isCalibration && items.length === 0 ? (
        <Empty description="Chưa có tài sản/vị trí để in tem" style={{ marginTop: 80 }} />
      ) : (
        <div className="label-print-paper-wrap">
          <div className="a4-page">
            <div
              className={`label-grid${cutLines ? ' cut-lines' : ''}`}
              style={gridStyle}
            >
              {cells.map((item, index) => (item
                ? <LabelFace key={`${item.id}-${index}`} item={item} template={template} />
                : <div
                    key={`empty-${index}`}
                    className={`asset-label${isCalibration ? ' asset-label--calibration' : ''}`}
                    style={{ width: `${template.widthMm}mm`, height: `${template.heightMm}mm` }}
                  />
              ))}
            </div>
          </div>
        </div>
      )}
    </div>
  );
}
