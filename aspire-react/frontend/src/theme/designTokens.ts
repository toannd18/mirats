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
