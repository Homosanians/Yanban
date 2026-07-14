import { useContext } from "react";
import { ThemeContext } from "./context";
import type { ThemeContextValue } from "./context";

export function useTheme(): ThemeContextValue {
  const ctx = useContext(ThemeContext);
  if (!ctx) throw new Error("useTheme must be used inside a ThemeProvider.");
  return ctx;
}
