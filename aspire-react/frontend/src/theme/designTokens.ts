import type { ThemeConfig } from 'antd';

const palette = {
  primary: '#0F172A',
  onPrimary: '#FFFFFF',
  secondary: '#334155',
  accent: '#0369A1',
  onAccent: '#FFFFFF',
  background: '#F8FAFC',
  foreground: '#020617',
  card: '#FFFFFF',
  muted: '#E8ECF1',
  mutedForeground: '#475569',
  border: '#E2E8F0',
  destructive: '#DC2626',
  onDestructive: '#FFFFFF',
  // T-TOKEN1 — màu UI lặp lại rải rác trong pages (trước đây hex literal khắp nơi):
  labelGray: '#8c8c8c',        // icon/label meta (creator, environment...) — icon, không phải text chính
  warningAmber: '#fa8c16',     // cảnh báo tồn kho / thu hồi / lưu trữ / statistic "Sắp hết"
  stockSuccessBg: '#f6ffed',   // nền card tồn kho (stock summary)
  stockSuccessBorder: '#b7eb8f', // viền card tồn kho
  // FE-R3 — token hóa màu phát sinh ở Maintenance pages (audit 2026-08-28):
  success: '#16A34A',          // icon "đã lưu" (CheckCircle) — khớp theme colorSuccess; 3.30:1 đạt AA graphics (3:1); #52c41a cũ chỉ 2.27:1
  accentPurple: '#722ed1',     // icon accent "Tiêu chuẩn kỹ thuật" (Experiment) — 6.94:1 đạt AA text
};

export const designTokens: ThemeConfig = {
  token: {
    colorPrimary: palette.accent,
    colorInfo: palette.accent,
    colorLink: palette.accent,
    colorError: palette.destructive,
    colorSuccess: '#16A34A',
    colorWarning: '#D97706',
    colorBgLayout: palette.background,
    colorBgContainer: palette.card,
    colorBgElevated: palette.card,
    colorText: palette.foreground,
    colorTextSecondary: palette.mutedForeground,
    colorTextTertiary: '#64748B',
    colorBorder: palette.border,
    colorBorderSecondary: palette.muted,
    fontFamily: "'Plus Jakarta Sans', -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, 'Helvetica Neue', Arial, sans-serif",
    fontWeightStrong: 700,
    fontSize: 14,
    borderRadius: 8,
    borderRadiusLG: 12,
    borderRadiusSM: 6,
    controlHeight: 32,
    boxShadow: '0 4px 6px rgba(0, 0, 0, 0.1)',
    boxShadowSecondary: '0 10px 15px rgba(0, 0, 0, 0.1)',
    boxShadowTertiary: '0 20px 25px rgba(0, 0, 0, 0.15)',
  },
  components: {
    Layout: {
      headerBg: palette.card,
      bodyBg: palette.background,
      siderBg: palette.primary,
    },
    Menu: {
      darkItemBg: palette.primary,
      // antd default darkSubMenuItemBg = #000c17 (đen-xanh đậm) áp cho khối con của
      // submenu inline → tạo khối nền xám đậm lệch tông so với Sider (#0F172A).
      // Đồng bộ về đúng màu nền menu gốc: khối con "trong suốt" hòa vào Sider.
      darkSubMenuItemBg: palette.primary,
      // Popup (Sider collapsed) cũng đồng màu nền Sider để không tạo khối lệch tông.
      darkPopupBg: palette.primary,
      // Contrast (WCAG): khóa tường minh màu chữ/icon/chevron của mọi submenu-title
      // (nghỉ/hover/selected/open) sang trắng/xám sáng. Mặc định antd bám theo
      // colorPrimary (#0F172A — màu TỐI) cho selected/hover; nếu theme lệch, chữ
      // "Vật tư"/"Quản trị" sẽ chìm vào nền tối. Set cứng để luôn sáng rõ.
      darkItemColor: '#CBD5E1',
      darkItemHoverColor: '#FFFFFF',
      darkItemSelectedColor: '#FFFFFF',
      darkItemSelectedBg: palette.accent,
      darkItemHoverBg: 'rgba(255, 255, 255, 0.08)',
    },
    Button: {
      fontWeight: 600,
    },
    Table: {
      headerBg: palette.muted,
      headerColor: palette.foreground,
    },
  },
};

/** Semantic text colors (WCAG-safe) — dùng thay hex literal rải rác trong pages. */
export const textColors = {
  primary: palette.foreground,
  secondary: palette.mutedForeground,
  tertiary: '#64748B',
} as const;

export const statusColors = {
  ready: '#1677ff',
  active: '#52c41a',
  overdue: '#dc2626',
  closed: '#8c8c8c',
  pending: '#d48806',
};

export const assetStatusColors: Record<string, string> = {
  Pending: statusColors.ready,
  Deployed: statusColors.active,
  Archived: statusColors.closed,
};

/**
 * T-TOKEN1 — semantic UI colors dùng chung (thay hex literal rải rác trong pages).
 * Giữ NGUYÊN giá trị màu (chỉ tập trung 1 nguồn) — việc đổi tông màu cho AA là
 * quyết định thiết kế riêng, không làm chung trong task token hóa này.
 */
export const uiColors = {
  /** Icon/label meta gray (creator, environment…) — icon 12-13px, không phải text chính. */
  labelGray: palette.labelGray,
  /** Cảnh báo tồn kho / thu hồi / lưu trữ / statistic "Sắp hết". */
  warningAmber: palette.warningAmber,
  /** Nền + viền card tồn kho (stock summary). */
  stockSuccessBg: palette.stockSuccessBg,
  stockSuccessBorder: palette.stockSuccessBorder,
  /** [FE-R3] Icon "đã lưu" (CheckCircle) — khớp theme colorSuccess #16A34A. */
  success: palette.success,
  /** [FE-R3] Icon accent tím "Tiêu chuẩn kỹ thuật" (ExperimentOutlined). */
  accentPurple: palette.accentPurple,
} as const;

/**
 * Badge gradient nền icon trên Card list (theme/designTokens là nguồn duy nhất).
 * Trước đây từng ListPage tự khai báo linear-gradient riêng → lệch tông.
 */
export const cardBadgeGradients = {
  /** Blue (Component / License / Maintenance). */
  blue: 'linear-gradient(135deg, #f0f5ff 0%, #adc6ff 100%)',
  /** Light blue (Consumable). */
  lightBlue: 'linear-gradient(135deg, #e6f4ff 0%, #bae0ff 100%)',
  /** Purple (Accessory). */
  purple: 'linear-gradient(135deg, #f0e6ff 0%, #d4baff 100%)',
} as const;
