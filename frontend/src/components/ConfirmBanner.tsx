import { useState } from "react";
import { MailWarning, X } from "lucide-react";
import { resendConfirmation } from "../auth/authApi";
import { useAuth } from "../auth/useAuth";

/**
 * The nag. Confirming is not a gate — the app underneath works perfectly well unconfirmed — so this
 * is a strip you can dismiss, not a wall you have to climb.
 *
 * Dismissal is per-session state and deliberately not persisted: the reminder should come back
 * tomorrow, because the address is still unproven.
 */
export function ConfirmBanner() {
  const { user } = useAuth();
  const [dismissed, setDismissed] = useState(false);
  const [state, setState] = useState<"idle" | "sending" | "sent" | "failed">("idle");

  if (!user || user.emailConfirmed || dismissed) return null;

  const resend = async () => {
    setState("sending");
    try {
      await resendConfirmation();
      setState("sent");
    } catch {
      setState("failed");
    }
  };

  return (
    <div className="banner" role="status">
      <MailWarning size={16} />
      <span className="grow">
        Confirm your email to secure your account. Sent to <strong>{user.email}</strong>.
      </span>

      {state === "sent" ? (
        <span className="faint">Sent — check your inbox.</span>
      ) : (
        <button className="secondary" onClick={() => void resend()} disabled={state === "sending"}>
          {state === "sending" ? "Sending…" : state === "failed" ? "Try again" : "Resend"}
        </button>
      )}

      <button className="icon-btn" aria-label="Dismiss" title="Dismiss" onClick={() => setDismissed(true)}>
        <X size={15} />
      </button>
    </div>
  );
}
