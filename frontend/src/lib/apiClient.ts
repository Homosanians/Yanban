const API_BASE = "/api";

// The access token lives only in memory (this module variable), never in
// localStorage/sessionStorage. The refresh token lives in an httpOnly cookie
// the browser attaches automatically; JS never sees it.
let accessToken: string | null = null;
let expiresAt = 0;
let onUnauthorized: (() => void) | null = null;
let refreshInFlight: Promise<string | null> | null = null;

/** Treat a token as spent this long before it actually expires, so nothing races the clock. */
const EXPIRY_MARGIN_MS = 60_000;

export function setAccessToken(token: string | null, tokenExpiresAt?: string): void {
  accessToken = token;
  expiresAt = token && tokenExpiresAt ? Date.parse(tokenExpiresAt) : 0;
}

export function getAccessToken(): string | null {
  return accessToken;
}

export function setUnauthorizedHandler(handler: () => void): void {
  onUnauthorized = handler;
}

export class ApiError extends Error {
  status: number;
  constructor(status: number, message: string) {
    super(message);
    this.status = status;
  }
}

async function parseError(res: Response): Promise<string> {
  try {
    const data = await res.json();
    // ProblemDetails: `detail` carries the specific message, `title` the generic one.
    return data.detail ?? data.title ?? res.statusText;
  } catch {
    return res.statusText;
  }
}

// Single-flight refresh: concurrent 401s share one refresh call.
export function refreshAccessToken(): Promise<string | null> {
  if (!refreshInFlight) {
    refreshInFlight = fetch(`${API_BASE}/auth/refresh`, {
      method: "POST",
      credentials: "include",
    })
      .then(async (res) => {
        if (!res.ok) return null;
        const data = (await res.json()) as { accessToken: string; accessTokenExpiresAt: string };
        setAccessToken(data.accessToken, data.accessTokenExpiresAt);
        return accessToken;
      })
      .catch(() => null)
      .finally(() => {
        refreshInFlight = null;
      });
  }
  return refreshInFlight;
}

/**
 * A token that is valid *now*, refreshing first if the current one is at or near expiry.
 *
 * REST calls don't need this: they discover expiry as a 401 and retry. A WebSocket cannot.
 * The hub authenticates once, at the handshake, and the server hangs the socket up the moment
 * the token expires (CloseOnAuthenticationExpiration, M7). SignalR then reconnects — and if the
 * token factory hands back the same expired token, that reconnect 401s and retries forever.
 * So the reconnect path must ask for a fresh token, not the last one it happened to see.
 */
export async function getFreshAccessToken(): Promise<string> {
  if (accessToken && Date.now() < expiresAt - EXPIRY_MARGIN_MS) return accessToken;
  return (await refreshAccessToken()) ?? "";
}

interface RequestOptions {
  method?: string;
  body?: unknown;
  auth?: boolean;
  /** Extra headers — If-Match on the one endpoint that demands it (ADR-13). */
  headers?: Record<string, string>;
}

export async function apiFetch<T>(path: string, options: RequestOptions = {}): Promise<T> {
  const { method = "GET", body, auth = true, headers: extraHeaders } = options;

  const doFetch = (): Promise<Response> => {
    const headers: Record<string, string> = { ...extraHeaders };
    if (body !== undefined) headers["Content-Type"] = "application/json";
    if (auth && accessToken) headers["Authorization"] = `Bearer ${accessToken}`;
    return fetch(`${API_BASE}${path}`, {
      method,
      credentials: "include",
      headers,
      body: body !== undefined ? JSON.stringify(body) : undefined,
    });
  };

  let res = await doFetch();

  // On 401, try a single silent refresh, then retry the request once.
  if (res.status === 401 && auth) {
    const newToken = await refreshAccessToken();
    if (newToken) {
      res = await doFetch();
    } else {
      setAccessToken(null);
      onUnauthorized?.();
    }
  }

  if (!res.ok) {
    throw new ApiError(res.status, await parseError(res));
  }

  if (res.status === 204) return undefined as T;
  return (await res.json()) as T;
}
