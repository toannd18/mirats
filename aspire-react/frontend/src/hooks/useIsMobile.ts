import { Grid } from 'antd';

/**
 * Shared mobile breakpoint hook — single source of truth for "is this a phone-width
 * viewport?" across the app (ST7b pattern from MaintenanceTable).
 *
 * Uses AntD's Grid.useBreakpoint() (SSR-safe: screens is {} on first render → md is
 * undefined → NOT mobile, matching desktop-first rendering) and returns true when the
 * `md` breakpoint (≥768px) is not met.
 *
 * Replaces the repeated `const screens = useBreakpoint(); const isMobile = !screens.md;`
 * pair that was copy-pasted in ~10 modals/pages.
 */
export function useIsMobile(): boolean {
  const screens = Grid.useBreakpoint();
  return screens.md === false;
}
