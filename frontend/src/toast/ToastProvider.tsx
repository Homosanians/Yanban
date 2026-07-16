import { useCallback, useMemo, useRef, useState } from "react";
import type { ReactNode } from "react";
import { X } from "lucide-react";
import { ToastContext } from "./context";
import type { Toast, ToastContextValue, ToastTone } from "./context";

const DISMISS_AFTER = 6000;

/**
 * One place that owns transient messages.
 *
 * Toasts auto-dismiss but can be dismissed early. They stack rather than replace: two things going
 * wrong at once is exactly when you least want the first message eaten.
 */
export function ToastProvider({ children }: { children: ReactNode }) {
  const [toasts, setToasts] = useState<Toast[]>([]);
  const nextId = useRef(0);

  const dismiss = useCallback((id: number) => {
    setToasts((current) => current.filter((t) => t.id !== id));
  }, []);

  const show = useCallback(
    (message: string, tone: ToastTone = "error") => {
      const id = nextId.current++;
      setToasts((current) => [...current, { id, message, tone }]);
      setTimeout(() => dismiss(id), DISMISS_AFTER);
    },
    [dismiss],
  );

  const value = useMemo<ToastContextValue>(() => ({ show }), [show]);

  return (
    <ToastContext.Provider value={value}>
      {children}
      <div className="toasts" role="status" aria-live="polite">
        {toasts.map((t) => (
          <div key={t.id} className={`toast ${t.tone}`}>
            <span>{t.message}</span>
            <button className="icon-btn" aria-label="Dismiss" onClick={() => dismiss(t.id)}>
              <X size={14} />
            </button>
          </div>
        ))}
      </div>
    </ToastContext.Provider>
  );
}
