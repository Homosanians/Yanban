import { useCallback, useEffect, useMemo, useState } from "react";
import type { ReactNode } from "react";
import { refreshAccessToken, setAccessToken, setUnauthorizedHandler } from "../lib/apiClient";
import {
  fetchMe,
  login as apiLogin,
  logout as apiLogout,
  register as apiRegister,
} from "./authApi";
import { AuthContext } from "./context";
import type { AuthContextValue, AuthStatus } from "./context";
import type { User } from "../types";

export function AuthProvider({ children }: { children: ReactNode }) {
  const [status, setStatus] = useState<AuthStatus>("loading");
  const [user, setUser] = useState<User | null>(null);

  const clearAuth = useCallback(() => {
    setAccessToken(null);
    setUser(null);
    setStatus("anonymous");
  }, []);

  // On load: silently refresh via the httpOnly cookie, then load the profile.
  // Restores the session across reloads without persisting the access token.
  useEffect(() => {
    let cancelled = false;
    setUnauthorizedHandler(clearAuth);
    (async () => {
      const token = await refreshAccessToken();
      if (cancelled) return;
      if (!token) {
        setStatus("anonymous");
        return;
      }
      try {
        const me = await fetchMe();
        if (cancelled) return;
        setUser(me);
        setStatus("authenticated");
      } catch {
        if (!cancelled) clearAuth();
      }
    })();
    return () => {
      cancelled = true;
    };
  }, [clearAuth]);

  const login = useCallback(async (email: string, password: string) => {
    await apiLogin(email, password);
    setUser(await fetchMe());
    setStatus("authenticated");
  }, []);

  const register = useCallback(
    async (email: string, password: string, displayName: string) => {
      await apiRegister(email, password, displayName);
      setUser(await fetchMe());
      setStatus("authenticated");
    },
    [],
  );

  const logout = useCallback(async () => {
    try {
      await apiLogout();
    } finally {
      clearAuth();
    }
  }, [clearAuth]);

  const refreshUser = useCallback(async () => {
    setUser(await fetchMe());
  }, []);

  const value = useMemo<AuthContextValue>(
    () => ({ status, user, login, register, logout, refreshUser }),
    [status, user, login, register, logout, refreshUser],
  );

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}
