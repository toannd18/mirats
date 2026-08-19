import Keycloak from 'keycloak-js';

const keycloakConfig = {
  url: import.meta.env.VITE_KEYCLOAK_URL || 'https://localhost:8080',
  realm: import.meta.env.VITE_KEYCLOAK_REALM || 'aspire-react',
  clientId: import.meta.env.VITE_KEYCLOAK_CLIENT_ID || 'frontend',
};

const keycloak = new Keycloak(keycloakConfig);

let isInitialized = false;
let initPromise: Promise<boolean> | null = null;

export const initKeycloak = async (): Promise<boolean> => {
  // Guard: Keycloak instance can only be initialized once.
  // React StrictMode double-mounts components in dev, so this prevents the second call.
  if (isInitialized) return keycloak.authenticated ?? false;

  // Deduplicate concurrent calls: return the same promise if init is already in progress
  if (initPromise) return initPromise;

  initPromise = (async () => {
    try {
      const authenticated = await keycloak.init({
        onLoad: 'login-required',
        pkceMethod: 'S256',
      });

      if (authenticated) {
        // Auto-refresh token 30s before expiry
        keycloak.onTokenExpired = () => {
          keycloak.updateToken(30).catch(() => {
            console.error('Failed to refresh token');
          });
        };
      }

      isInitialized = true;
      return authenticated;
    } catch (error) {
      console.error('Keycloak initialization failed:', error);
      isInitialized = true;
      return false;
    }
  })();

  return initPromise;
};

export const login = () => keycloak.login();
export const logout = () => keycloak.logout({ redirectUri: window.location.origin });
export const getToken = (): string | undefined => keycloak.token;
export const isAuthenticated = (): boolean => keycloak.authenticated ?? false;

/**
 * The Keycloak subject (SSO identity) of the current token.
 * NOTE: this is the Keycloak user id — NEVER use it as a local user FK or to build
 * profile routes. It is only used here to detect when the signed-in identity changes
 * so module-level caches (e.g. useCurrentUser) can be keyed/refreshed per user.
 */
export const getCurrentSub = (): string => keycloak.tokenParsed?.sub || '';

export const getUserInfo = () => ({
  id: keycloak.tokenParsed?.sub || '',
  username: keycloak.tokenParsed?.preferred_username || '',
  email: keycloak.tokenParsed?.email || '',
  firstName: keycloak.tokenParsed?.given_name || '',
  lastName: keycloak.tokenParsed?.family_name || '',
});

export const hasRealmRole = (role: string): boolean =>
  keycloak.hasRealmRole(role);

/**
 * Mirrors the backend `ICompanyScopeService.IsSuperUser()` EXACTLY 1-1:
 *   realm_access contains "superuser" OR "admin" (substring, same as the server's
 *   `realmAccess.Contains(...)` on the raw JSON claim) OR a "permission" claim "superuser".
 * Used to decide who sees destructive actions (e.g. the Maintenance delete button).
 * NOTE: keep in sync with the backend — do NOT add roles here that the backend rejects.
 */
export const isSuperUser = (): boolean => {
  const t = keycloak.tokenParsed as Record<string, unknown> | undefined;
  if (!t) return false;
  // Exact realm-role membership (mirrors backend RealmAccessHelper): a role merely containing
  // "admin"/"superuser" as a substring must NOT count.
  const permission = t.permission;
  if (permission === 'superuser' || (Array.isArray(permission) && permission.includes('superuser'))) return true;
  const roles = (t.realm_access as { roles?: unknown } | undefined)?.roles;
  return Array.isArray(roles) && (roles as string[]).some(r => r === 'admin' || r === 'superuser');
};

export default keycloak;