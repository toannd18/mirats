import axios from 'axios';
import apiClient from '../../../services/api-client';
import { applyAccessToken } from './auth';

/**
 * [AUTH Phase 3 fix — E2E-found] ALL authenticated passkey endpoints MUST go through
 * apiClient (its request interceptor attaches the Bearer token). Plain axios sent NO
 * Authorization header → 401 on GET /auth/passkeys + register/options (caught live by the
 * virtual-authenticator E2E: `auth=NO` on every failing request). Only ANONYMOUS endpoints
 * (status, login/options, login) may use plain axios.
 */

// ---------- base64url <-> ArrayBuffer (WebAuthn wire format) ----------

export function bufferToBase64Url(buffer: ArrayBuffer): string {
  const bytes = new Uint8Array(buffer);
  let binary = '';
  for (let i = 0; i < bytes.length; i++) binary += String.fromCharCode(bytes[i]);
  return btoa(binary).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
}

export function base64UrlToBuffer(value: string): ArrayBuffer {
  const binary = atob(value.replace(/-/g, '+').replace(/_/g, '/'));
  const bytes = new Uint8Array(binary.length);
  for (let i = 0; i < binary.length; i++) bytes[i] = binary.charCodeAt(i);
  return bytes.buffer;
}

// ---------- status ----------

export async function isPasskeyEnabled(): Promise<boolean> {
  try {
    const res = await axios.get('/api/v1/auth/passkeys/status', { timeout: 8000 });
    return res.data?.data?.enabled === true;
  } catch {
    return false; // fail-closed: never offer passkey when the status probe fails
  }
}

// ---------- registration (logged-in user, AccountPage) ----------

export async function registerPasskey(name: string): Promise<{ ok: boolean; message?: string }> {
  const optionsRes = await apiClient.post('/auth/passkeys/register/options');
  const options = typeof optionsRes.data === 'string' ? JSON.parse(optionsRes.data) : optionsRes.data;
  const publicKey = parseCreationOptions(options);

  const credential = (await navigator.credentials.create({ publicKey })) as PublicKeyCredential | null;
  if (!credential) return { ok: false, message: 'Không tạo được passkey (huỷ hoặc không hỗ trợ).' };

  const attestationJson = serializeAttestation(credential);
  try {
    const done = await apiClient.post('/auth/passkeys/register', { attestationJson, name });
    return { ok: done.data?.status === 'success' };
  } catch (err: unknown) {
    const e = err as { response?: { data?: { message?: string } } };
    return { ok: false, message: e?.response?.data?.message || 'Đăng ký passkey thất bại.' };
  }
}

// ---------- assertion (login page) ----------

export async function loginWithPasskey(username?: string): Promise<{ ok: boolean; mustChangePassword?: boolean; message?: string }> {
  const optionsRes = await axios.post(
    '/api/v1/auth/passkeys/login/options',
    { username: username ?? null },
    { timeout: 15000 }
  );
  const options = typeof optionsRes.data === 'string' ? JSON.parse(optionsRes.data) : optionsRes.data;
  const publicKey = parseRequestOptions(options);

  const assertion = (await navigator.credentials.get({ publicKey })) as PublicKeyCredential | null;
  if (!assertion) return { ok: false, message: 'Xác thực passkey bị huỷ.' };

  const assertionJson = serializeAssertion(assertion);
  try {
    const done = await axios.post('/api/v1/auth/passkeys/login', { assertionJson }, { withCredentials: true, timeout: 15000 });
    applyAccessToken(done.data.accessToken);
    return { ok: true, mustChangePassword: done.data.mustChangePassword === true };
  } catch (err: unknown) {
    const e = err as { response?: { data?: { message?: string } } };
    return { ok: false, message: e?.response?.data?.message || 'Xác thực passkey không thành công.' };
  }
}

// ---------- account page listing ----------

export interface PasskeyItem {
  id: string;
  name: string;
  createdAt: string;
  lastUsedAt: string | null;
}

export async function listPasskeys(): Promise<PasskeyItem[]> {
  const res = await apiClient.get('/auth/passkeys');
  return res.data?.data ?? [];
}

export async function deletePasskey(id: string): Promise<void> {
  await apiClient.delete(`/auth/passkeys/${id}`);
}

// ---------- WebAuthn option/response shaping ----------

type Json = Record<string, unknown>;

function parseCreationOptions(options: Json): PublicKeyCredentialCreationOptions {
  const challenge = base64UrlToBuffer(options.challenge as string);
  const user = options.user as Json;
  const exclude = (options.excludeCredentials as Json[] | undefined) ?? [];
  return {
    challenge,
    rp: options.rp as PublicKeyCredentialRpEntity,
    user: {
      id: base64UrlToBuffer(user.id as string),
      name: user.name as string,
      displayName: user.displayName as string
    },
    pubKeyCredParams: options.pubKeyCredParams as unknown as PublicKeyCredentialParameters[],
    timeout: options.timeout as number,
    excludeCredentials: exclude.map(c => ({
      type: c.type as string,
      id: base64UrlToBuffer(c.id as string)
    })) as PublicKeyCredentialDescriptor[],
    authenticatorSelection: options.authenticatorSelection as PublicKeyCredentialCreationOptions['authenticatorSelection'],
    attestation: options.attestation as AttestationConveyancePreference
  };
}

function parseRequestOptions(options: Json): PublicKeyCredentialRequestOptions {
  const allow = (options.allowCredentials as Json[] | undefined) ?? [];
  return {
    challenge: base64UrlToBuffer(options.challenge as string),
    rpId: options.rpId as string,
    timeout: options.timeout as number,
    userVerification: options.userVerification as UserVerificationRequirement,
    allowCredentials: allow.map(c => ({
      type: c.type as string,
      id: base64UrlToBuffer(c.id as string)
    })) as PublicKeyCredentialDescriptor[]
  };
}

function serializeAttestation(credential: PublicKeyCredential): string {
  const response = credential.response as AuthenticatorAttestationResponse;
  return JSON.stringify({
    id: credential.id,
    rawId: bufferToBase64Url(credential.rawId),
    type: credential.type,
    response: {
      attestationObject: bufferToBase64Url(response.attestationObject),
      clientDataJSON: bufferToBase64Url(response.clientDataJSON)
    }
  });
}

function serializeAssertion(credential: PublicKeyCredential): string {
  const response = credential.response as AuthenticatorAssertionResponse;
  return JSON.stringify({
    id: credential.id,
    rawId: bufferToBase64Url(credential.rawId),
    type: credential.type,
    response: {
      authenticatorData: bufferToBase64Url(response.authenticatorData),
      clientDataJSON: bufferToBase64Url(response.clientDataJSON),
      signature: bufferToBase64Url(response.signature),
      userHandle: response.userHandle ? bufferToBase64Url(response.userHandle) : null
    }
  });
}
