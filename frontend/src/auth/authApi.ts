import { apiFetch, setAccessToken } from "../lib/apiClient";
import type { AccessTokenResponse, User } from "../types";

export async function register(email: string, password: string, displayName: string): Promise<void> {
  const res = await apiFetch<AccessTokenResponse>("/auth/register", {
    method: "POST",
    body: { email, password, displayName },
    auth: false,
  });
  setAccessToken(res.accessToken, res.accessTokenExpiresAt);
}

export async function login(email: string, password: string): Promise<void> {
  const res = await apiFetch<AccessTokenResponse>("/auth/login", {
    method: "POST",
    body: { email, password },
    auth: false,
  });
  setAccessToken(res.accessToken, res.accessTokenExpiresAt);
}

export async function logout(): Promise<void> {
  await apiFetch<void>("/auth/logout", { method: "POST", auth: false });
  setAccessToken(null);
}

export function fetchMe(): Promise<User> {
  return apiFetch<User>("/me");
}

/** Anonymous: the link is followed out of a mail client, which carries no session. */
export function confirmEmail(token: string): Promise<void> {
  return apiFetch<void>("/auth/confirm-email", { method: "POST", body: { token }, auth: false });
}

export function resendConfirmation(): Promise<void> {
  return apiFetch<void>("/auth/resend-confirmation", { method: "POST" });
}
