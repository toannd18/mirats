/**
 * Only allow same-app absolute paths after login. Reject open-redirect payloads such as
 * //evil.com, https://evil.com and javascript:alert(1). Invalid values fall back to home.
 */
export const SAFE_REDIRECT_FALLBACK = '/';

export function safeInternalPath(value: unknown, fallback = SAFE_REDIRECT_FALLBACK): string {
  if (typeof value !== 'string') return fallback;
  const candidate = value.trim();
  if (!candidate.startsWith('/')) return fallback;

  // Protocol-relative URL (including ///evil.com).
  if (candidate.startsWith('//')) return fallback;

  // Browsers may treat backslashes like slashes in URLs.
  if (candidate.includes('\\')) return fallback;

  // Control characters / whitespace in the middle are not a valid in-app path.
  for (const ch of candidate) {
    const code = ch.charCodeAt(0);
    if (code <= 0x1f || code === 0x7f) return fallback;
  }

  // Defence in depth: an absolute URL can never be an internal path, but keep the check explicit.
  if (/^[a-z][a-z0-9+.-]*:/i.test(candidate)) return fallback;

  return candidate;
}
