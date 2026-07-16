import { useEffect, useRef, useState } from "react";
import type { KeyboardEvent } from "react";
import { Calendar, ChevronLeft, ChevronRight } from "lucide-react";
import { formatDue } from "../lib/due";

interface Props {
  value: string; // "YYYY-MM-DD" or ""
  onChange: (value: string) => void;
  disabled?: boolean;
  ariaLabel?: string;
}

const WEEKDAYS = ["Mo", "Tu", "We", "Th", "Fr", "Sa", "Su"];

// A due date is a UTC calendar day (see lib/due.ts), so every date here is built and compared in
// UTC. Reading local time would drift the day for browsers west of Greenwich.
const key = (year: number, month: number, day: number): string =>
  `${year}-${String(month + 1).padStart(2, "0")}-${String(day).padStart(2, "0")}`;

const todayKey = (): string => {
  const n = new Date();
  return key(n.getUTCFullYear(), n.getUTCMonth(), n.getUTCDate());
};

/**
 * A calendar-day picker to replace the native date input. Value stays a bare "YYYY-MM-DD" string,
 * the same shape the card update expects. Escape and outside clicks close it.
 */
export function DatePicker({ value, onChange, disabled, ariaLabel }: Props) {
  const [open, setOpen] = useState(false);
  const [view, setView] = useState(() => {
    const base = value ? new Date(`${value}T00:00:00Z`) : new Date();
    return { year: base.getUTCFullYear(), month: base.getUTCMonth() };
  });
  const rootRef = useRef<HTMLDivElement>(null);
  const triggerRef = useRef<HTMLButtonElement>(null);

  useEffect(() => {
    if (!open) return;
    const onDown = (e: MouseEvent) => {
      if (!rootRef.current?.contains(e.target as Node)) setOpen(false);
    };
    document.addEventListener("mousedown", onDown);
    return () => document.removeEventListener("mousedown", onDown);
  }, [open]);

  // Escape closes the calendar. Handled here rather than on window, and only while open, so the
  // native event is stopped before the drawer's own window-level Escape closes the whole card.
  const onKeyDown = (e: KeyboardEvent) => {
    if (open && e.key === "Escape") {
      e.preventDefault();
      e.stopPropagation();
      setOpen(false);
      triggerRef.current?.focus();
    }
  };

  const openMenu = () => {
    if (disabled) return;
    const base = value ? new Date(`${value}T00:00:00Z`) : new Date();
    setView({ year: base.getUTCFullYear(), month: base.getUTCMonth() });
    setOpen(true);
  };

  const pick = (k: string) => {
    onChange(k);
    setOpen(false);
    triggerRef.current?.focus();
  };

  const first = new Date(Date.UTC(view.year, view.month, 1));
  const startWeekday = (first.getUTCDay() + 6) % 7; // Monday-based grid
  const daysInMonth = new Date(Date.UTC(view.year, view.month + 1, 0)).getUTCDate();
  const monthLabel = first.toLocaleDateString(undefined, { month: "long", year: "numeric", timeZone: "UTC" });
  const today = todayKey();

  const cells: (number | null)[] = [];
  for (let i = 0; i < startWeekday; i++) cells.push(null);
  for (let d = 1; d <= daysInMonth; d++) cells.push(d);

  const step = (delta: number) =>
    setView((v) => {
      const m = v.month + delta;
      if (m < 0) return { year: v.year - 1, month: 11 };
      if (m > 11) return { year: v.year + 1, month: 0 };
      return { year: v.year, month: m };
    });

  return (
    <div className="datepicker" ref={rootRef} onKeyDown={onKeyDown}>
      <button
        type="button"
        ref={triggerRef}
        className="dropdown-trigger"
        aria-haspopup="dialog"
        aria-expanded={open}
        aria-label={ariaLabel}
        disabled={disabled}
        onClick={() => (open ? setOpen(false) : openMenu())}
      >
        <span className={`dropdown-value${value ? "" : " placeholder"}`}>
          <Calendar size={15} />
          <span className="truncate">{value ? formatDue(`${value}T00:00:00Z`) : "No due date"}</span>
        </span>
      </button>

      {open && (
        <div className="calendar" role="dialog" aria-label="Choose a due date">
          <div className="calendar-head">
            <button type="button" className="icon-btn" aria-label="Previous month" onClick={() => step(-1)}>
              <ChevronLeft size={16} />
            </button>
            <span className="calendar-month">{monthLabel}</span>
            <button type="button" className="icon-btn" aria-label="Next month" onClick={() => step(1)}>
              <ChevronRight size={16} />
            </button>
          </div>
          <div className="calendar-grid">
            {WEEKDAYS.map((w) => (
              <span key={w} className="calendar-weekday">{w}</span>
            ))}
            {cells.map((d, i) => {
              if (d === null) return <span key={`b${i}`} />;
              const k = key(view.year, view.month, d);
              return (
                <button
                  key={k}
                  type="button"
                  className={`calendar-day${k === value ? " selected" : ""}${k === today ? " today" : ""}`}
                  onClick={() => pick(k)}
                >
                  {d}
                </button>
              );
            })}
          </div>
          <div className="calendar-foot">
            <button type="button" className="link" onClick={() => pick(today)}>Today</button>
            {value && (
              <button
                type="button"
                className="link"
                onClick={() => {
                  onChange("");
                  setOpen(false);
                }}
              >
                Clear
              </button>
            )}
          </div>
        </div>
      )}
    </div>
  );
}
