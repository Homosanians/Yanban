import { Monitor, Moon, Sun } from "lucide-react";
import { useTheme } from "../theme/useTheme";
import type { ThemeMode } from "../theme/context";

const OPTIONS: { mode: ThemeMode; label: string; Icon: typeof Sun }[] = [
  { mode: "light", label: "Light theme", Icon: Sun },
  { mode: "dark", label: "Dark theme", Icon: Moon },
  { mode: "system", label: "Match system theme", Icon: Monitor },
];

export function ThemeToggle() {
  const { mode, setMode } = useTheme();

  return (
    <div className="segmented" role="group" aria-label="Theme">
      {OPTIONS.map(({ mode: value, label, Icon }) => (
        <button
          key={value}
          type="button"
          // aria-pressed, not a class: the selected state is a fact about the control, and this
          // way the styling and the screen reader read from the same source.
          aria-pressed={mode === value}
          aria-label={label}
          title={label}
          onClick={() => setMode(value)}
        >
          <Icon size={14} strokeWidth={2} />
        </button>
      ))}
    </div>
  );
}
