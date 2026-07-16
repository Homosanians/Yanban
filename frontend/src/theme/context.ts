import { createContext } from "react";

/** What the user picked. "system" is a deferral, not a colour. */
export type ThemeMode = "system" | "light" | "dark";

/** What the app actually renders as, once "system" has been resolved. */
export type ResolvedTheme = "light" | "dark";

export interface ThemeContextValue {
  mode: ThemeMode;
  resolved: ResolvedTheme;
  setMode: (mode: ThemeMode) => void;
}

export const ThemeContext = createContext<ThemeContextValue | null>(null);

/** Shared with the pre-paint script in index.html; keep the two in step. */
export const THEME_STORAGE_KEY = "yanban.theme";
