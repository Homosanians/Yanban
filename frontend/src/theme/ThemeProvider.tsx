import { useCallback, useEffect, useMemo, useState } from "react";
import type { ReactNode } from "react";
import { THEME_STORAGE_KEY, ThemeContext } from "./context";
import type { ResolvedTheme, ThemeContextValue, ThemeMode } from "./context";

const darkQuery = () => window.matchMedia("(prefers-color-scheme: dark)");

const readStoredMode = (): ThemeMode => {
  const stored = localStorage.getItem(THEME_STORAGE_KEY);
  return stored === "light" || stored === "dark" ? stored : "system";
};

/**
 * Owns the light/dark decision.
 *
 * The provider resolves "system" down to a concrete theme and stamps *that* on &lt;html&gt;, so the
 * stylesheet only ever has to know about `[data-theme="dark"]` — no CSS anywhere reasons about
 * "system". The same stamp is applied before first paint by the inline script in index.html;
 * this keeps it true from then on.
 */
export function ThemeProvider({ children }: { children: ReactNode }) {
  const [mode, setModeState] = useState<ThemeMode>(readStoredMode);
  const [systemTheme, setSystemTheme] = useState<ResolvedTheme>(() =>
    darkQuery().matches ? "dark" : "light",
  );

  // "System" has to mean *live*: someone flipping their OS to dark expects the app to follow
  // there and then, not on the next reload.
  useEffect(() => {
    const query = darkQuery();
    const onChange = (e: MediaQueryListEvent) => setSystemTheme(e.matches ? "dark" : "light");
    query.addEventListener("change", onChange);
    return () => query.removeEventListener("change", onChange);
  }, []);

  const resolved: ResolvedTheme = mode === "system" ? systemTheme : mode;

  useEffect(() => {
    const root = document.documentElement;
    root.dataset.theme = resolved;
    // Tells the browser to render its own furniture — scrollbars, date pickers, autofill — to
    // match. Without it a dark board keeps a bright white scrollbar down its side.
    root.style.colorScheme = resolved;
  }, [resolved]);

  const setMode = useCallback((next: ThemeMode) => {
    setModeState(next);
    // "system" is stored as the absence of a choice, which is exactly what the pre-paint script
    // falls back to reading.
    if (next === "system") localStorage.removeItem(THEME_STORAGE_KEY);
    else localStorage.setItem(THEME_STORAGE_KEY, next);
  }, []);

  const value = useMemo<ThemeContextValue>(
    () => ({ mode, resolved, setMode }),
    [mode, resolved, setMode],
  );

  return <ThemeContext.Provider value={value}>{children}</ThemeContext.Provider>;
}
