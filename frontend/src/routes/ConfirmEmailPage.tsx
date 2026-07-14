import { useEffect, useRef, useState } from "react";
import { Link, useSearchParams } from "react-router-dom";
import { CheckCircle2, XCircle } from "lucide-react";
import { confirmEmail } from "../auth/authApi";
import { useAuth } from "../auth/useAuth";

type State = "working" | "done" | "failed";

/**
 * Where the emailed link lands. Anonymous on purpose — it is followed out of a mail client, which
 * has no session, and possibly on a different device from the one that signed up.
 */
export function ConfirmEmailPage() {
  const [params] = useSearchParams();
  const { status, refreshUser } = useAuth();
  const [state, setState] = useState<State>("working");
  const [error, setError] = useState<string | null>(null);

  // React 18+ runs effects twice in dev StrictMode. The token is single-use, so a second redemption
  // would fail and paint an error over a confirmation that actually worked.
  const redeemed = useRef(false);

  useEffect(() => {
    const token = params.get("token");
    if (!token) {
      setState("failed");
      setError("This link is missing its token.");
      return;
    }
    if (redeemed.current) return;
    redeemed.current = true;

    void (async () => {
      try {
        await confirmEmail(token);
        setState("done");
        // If this tab happens to be signed in, the banner should vanish without a reload.
        if (status === "authenticated") await refreshUser();
      } catch (err) {
        setState("failed");
        setError(err instanceof Error ? err.message : "This link is invalid or has already been used.");
      }
    })();
  }, [params, status, refreshUser]);

  return (
    <div className="auth-shell">
      <div className="auth-card">
        <div className="wordmark">
          <span className="dot" />
          Yanban
        </div>

        {state === "working" && <p className="tagline">Confirming your email…</p>}

        {state === "done" && (
          <>
            <p className="confirm-icon ok"><CheckCircle2 size={34} /></p>
            <h1>Email confirmed</h1>
            <p className="tagline">Your address is verified. Nothing else to do.</p>
            <Link to="/">Go to your boards</Link>
          </>
        )}

        {state === "failed" && (
          <>
            <p className="confirm-icon bad"><XCircle size={34} /></p>
            <h1>That link did not work</h1>
            <p className="tagline">{error}</p>
            {/* A spent or expired link is not a dead end: sign in and the banner will offer a new one. */}
            <Link to="/">Go to your boards</Link>
          </>
        )}
      </div>
    </div>
  );
}
