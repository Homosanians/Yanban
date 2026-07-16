import { createContext } from "react";

export type ToastTone = "error" | "info";

export interface Toast {
  id: number;
  message: string;
  tone: ToastTone;
}

export interface ToastContextValue {
  /** Shows a toast. Returns nothing: a toast is a statement, not a question. */
  show: (message: string, tone?: ToastTone) => void;
}

export const ToastContext = createContext<ToastContextValue | null>(null);
