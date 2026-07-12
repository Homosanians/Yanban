import { apiFetch, setAccessToken } from "../lib/apiClient";
import type { AccessTokenResponse, User } from "../types";

export async function register(email: string, password: string, displayName: string): Promise<void> {
  const res = await apiFetch<AccessTokenResponse>("/auth/register", {
    method: "POST",
    body: { email, password, displayName },
    auth: false,
  });
  setAccessToken(res.accessToken);
}

export async function login(email: string, password: string): Promise<void> {
  const res = await apiFetch<AccessTokenResponse>("/auth/login", {
    method: "POST",
    body: { email, password },
    auth: false,
  });
  setAccessToken(res.accessToken);
}

export async function logout(): Promise<void> {
  await apiFetch<void>("/auth/logout", { method: "POST", auth: false });
  setAccessToken(null);
}

export function fetchMe(): Promise<User> {
  return apiFetch<User>("/me");
}
