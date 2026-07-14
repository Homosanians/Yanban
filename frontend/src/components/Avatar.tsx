import { colorFor } from "../lib/color";

interface Props {
  email: string;
  /** Shown in the tooltip alongside the email, when we know it. */
  name?: string;
  size?: "sm" | "lg";
  className?: string;
}

/**
 * Identity, with nothing stored: the letter is the first character of the email and the colour is
 * a pure function of the whole email. No avatar ever has to be uploaded, hosted or fetched.
 */
export function Avatar({ email, name, size, className }: Props) {
  const classes = ["avatar", size, className].filter(Boolean).join(" ");
  return (
    <span
      className={classes}
      style={{ background: colorFor(email) }}
      title={name ? `${name} · ${email}` : email}
    >
      {email.charAt(0)}
    </span>
  );
}
