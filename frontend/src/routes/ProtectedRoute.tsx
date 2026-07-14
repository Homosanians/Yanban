import type { ReactNode } from "react";
import { Navigate } from "react-router-dom";
import { useAuth } from "../auth/useAuth";
import { ConfirmBanner } from "../components/ConfirmBanner";

export function ProtectedRoute({ children }: { children: ReactNode }) {
  const { status } = useAuth();
  if (status === "loading") {
    return <p className="pad muted">Loading…</p>;
  }
  if (status === "anonymous") {
    return <Navigate to="/login" replace />;
  }
  // The banner lives here rather than in each page: it belongs on every authenticated screen, and
  // there is exactly one place that knows what "authenticated" means.
  return (
    <div className="app-shell">
      <ConfirmBanner />
      {children}
    </div>
  );
}
