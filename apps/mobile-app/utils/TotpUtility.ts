import NativeVaultManager from '@/specs/NativeVaultManager';

/**
 * Generates the current TOTP code for the given secret key.
 *
 * Delegates to the platform-native TOTP generator (Swift on iOS, Kotlin on
 * Android) so the React Native layer, the iOS Autofill extension, and the
 * Android Autofill service all share one RFC 6238 implementation. Standard
 * settings: HMAC-SHA1, 6 digits, 30-second period.
 *
 * @param secretKey - Base32-encoded TOTP secret
 * @returns The current 6-digit TOTP code, or empty string on error
 */
export async function generateTotpCode(secretKey: string): Promise<string> {
  try {
    const code = await NativeVaultManager.generateTotpCode(secretKey);
    return code ?? '';
  } catch (error) {
    console.error('Error generating TOTP code:', error);
    return '';
  }
}

/**
 * Parsed `otpauth://` URI components.
 */
export type OtpAuthUri = {
  /** "totp" or "hotp" — only "totp" is supported elsewhere in the app. */
  type: 'totp' | 'hotp';
  /** URL-decoded path component, typically "Issuer:account". */
  label: string;
  /** Base32 secret from the `secret` query parameter. */
  secret: string;
  /** Optional `issuer` query parameter. */
  issuer?: string;
};

/**
 * Parse an `otpauth://` URI per
 * https://github.com/google/google-authenticator/wiki/Key-Uri-Format.
 * 
 * Returns null when the input is not a valid `otpauth://` URI or is missing
 * a `secret` parameter. Does NOT validate the Base32 alphabet of the secret —
 * callers (e.g. `sanitizeSecretKey` in TotpEditor) handle that separately.
 */
export function parseOtpAuthUri(uri: string): OtpAuthUri | null {
  const trimmed = uri.trim();
  const prefix = 'otpauth://';
  if (trimmed.toLowerCase().slice(0, prefix.length) !== prefix) {
    return null;
  }

  const afterScheme = trimmed.slice(prefix.length);
  const slashIdx = afterScheme.indexOf('/');
  if (slashIdx < 0) {
    return null;
  }

  const typeRaw = afterScheme.slice(0, slashIdx).toLowerCase();
  if (typeRaw !== 'totp' && typeRaw !== 'hotp') {
    return null;
  }

  const rest = afterScheme.slice(slashIdx + 1);
  const queryIdx = rest.indexOf('?');
  const labelEncoded = queryIdx >= 0 ? rest.slice(0, queryIdx) : rest;
  const queryString = queryIdx >= 0 ? rest.slice(queryIdx + 1) : '';

  let label: string;
  try {
    label = decodeURIComponent(labelEncoded);
  } catch {
    label = labelEncoded;
  }

  const params = new URLSearchParams(queryString);
  const secret = params.get('secret');
  if (!secret) {
    return null;
  }

  const issuer = params.get('issuer');
  return {
    type: typeRaw,
    label,
    secret,
    ...(issuer ? { issuer } : {}),
  };
}
