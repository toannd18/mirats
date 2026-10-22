/**
 * Public origin used in QR payloads. Production/deployments MUST set VITE_PUBLIC_APP_URL
 * (e.g. https://assets.company.vn); local dev falls back to the browser origin.
 * Never hard-code a host in label/QR code.
 */
export function getPublicAppUrl(): string {
  const configured = (import.meta as unknown as { env?: Record<string, string | undefined> })
    .env?.VITE_PUBLIC_APP_URL?.trim();
  const base = configured || (typeof window !== 'undefined' ? window.location.origin : '');
  return base.replace(/\/+$/, '');
}

export function assetShortUrl(assetId: string): string {
  return `${getPublicAppUrl()}/a/${assetId}`;
}

export function systemPositionUrl(systemId: string, positionId: string): string {
  return `${getPublicAppUrl()}/systems/${systemId}?position=${encodeURIComponent(positionId)}`;
}
