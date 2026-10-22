export type LabelTemplateId = 'standard' | 'small' | 'rack';

export interface LabelTemplate {
  id: LabelTemplateId;
  name: string;
  sizeLabel: string;
  widthMm: number;
  heightMm: number;
  qrMm: number;
  cols: number;
  rows: number;
  /** A4 inner padding in mm (top/bottom, left/right). */
  padY: number;
  padX: number;
  showName: boolean;
  showOrg: boolean;
  showContext: boolean;
  codePt: number;
  namePt: number;
  orgPt: number;
  qrPadMm: number;
}

/**
 * Print geometry is in real millimetres. A4 is 210 x 297 mm.
 * The grid totals are deliberately exact at 100% scale:
 * - standard: 4*50 = 200 + 2*5  = 210; 11*25 = 275 + 2*11 = 297
 * - small:    5*38 = 190 + 2*10 = 210; 13*21 = 273 + 2*12 = 297
 * - rack:     3*70 = 210 + 0     = 210;  8*37 = 296 + 2*0.5 = 297
 */
export const LABEL_TEMPLATES: Record<LabelTemplateId, LabelTemplate> = {
  standard: {
    id: 'standard',
    name: 'Tiêu chuẩn',
    sizeLabel: '50 × 25 mm',
    widthMm: 50,
    heightMm: 25,
    qrMm: 20,
    cols: 4,
    rows: 11,
    padY: 11,
    padX: 5,
    showName: true,
    showOrg: true,
    showContext: false,
    codePt: 9,
    namePt: 7,
    orgPt: 6,
    qrPadMm: 0,
  },
  small: {
    id: 'small',
    name: 'Nhỏ',
    sizeLabel: '38 × 21 mm',
    widthMm: 38,
    heightMm: 21,
    qrMm: 16,
    cols: 5,
    rows: 13,
    padY: 12,
    padX: 10,
    showName: false,
    showOrg: false,
    showContext: false,
    codePt: 8,
    namePt: 6,
    orgPt: 5,
    qrPadMm: 0,
  },
  rack: {
    id: 'rack',
    name: 'Tủ rack',
    sizeLabel: '70 × 37 mm',
    widthMm: 70,
    heightMm: 37,
    qrMm: 28,
    cols: 3,
    rows: 8,
    padY: 0.5,
    padX: 0,
    showName: true,
    showOrg: false,
    showContext: true,
    codePt: 10,
    namePt: 7.5,
    orgPt: 6.5,
    qrPadMm: 0,
  },
};

export const LABEL_TEMPLATE_ORDER: LabelTemplateId[] = ['standard', 'small', 'rack'];

export function getLabelTemplate(id: string | null | undefined): LabelTemplate {
  return LABEL_TEMPLATES[(id as LabelTemplateId) in LABEL_TEMPLATES ? (id as LabelTemplateId) : 'standard'];
}

export function templateCapacity(t: LabelTemplate): number {
  return t.cols * t.rows;
}
