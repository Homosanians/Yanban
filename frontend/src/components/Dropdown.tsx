import { useEffect, useRef, useState } from "react";
import type { KeyboardEvent, ReactNode } from "react";
import { ChevronDown } from "lucide-react";

export interface DropdownOption {
  value: string;
  label: string;
  icon?: ReactNode;
}

interface Props {
  value: string;
  onChange: (value: string) => void;
  options: DropdownOption[];
  placeholder?: string;
  disabled?: boolean;
  ariaLabel?: string;
  className?: string;
}

/**
 * A styled single-select that reads like the native inputs but matches the app's look. Keyboard
 * and listbox behaviour mirror CommandPalette: arrow keys move a roving active row, Enter picks,
 * Escape closes. Outside clicks close it via a document listener, which behaves the same whether
 * the dropdown sits in a page, a panel, or the card drawer.
 */
export function Dropdown({
  value,
  onChange,
  options,
  placeholder = "Select",
  disabled,
  ariaLabel,
  className,
}: Props) {
  const [open, setOpen] = useState(false);
  const [active, setActive] = useState(0);
  const rootRef = useRef<HTMLDivElement>(null);
  const triggerRef = useRef<HTMLButtonElement>(null);
  const listRef = useRef<HTMLDivElement>(null);

  const selected = options.find((o) => o.value === value);

  useEffect(() => {
    if (!open) return;
    const onDown = (e: MouseEvent) => {
      if (!rootRef.current?.contains(e.target as Node)) setOpen(false);
    };
    document.addEventListener("mousedown", onDown);
    return () => document.removeEventListener("mousedown", onDown);
  }, [open]);

  // Keep the active row in view as the arrows move it past the visible edge.
  useEffect(() => {
    if (!open) return;
    (listRef.current?.children[active] as HTMLElement | undefined)?.scrollIntoView({ block: "nearest" });
  }, [open, active]);

  const openMenu = () => {
    if (disabled) return;
    const i = options.findIndex((o) => o.value === value);
    setActive(i < 0 ? 0 : i);
    setOpen(true);
  };

  const choose = (i: number) => {
    const opt = options[i];
    if (!opt) return;
    onChange(opt.value);
    setOpen(false);
    triggerRef.current?.focus();
  };

  const onKeyDown = (e: KeyboardEvent) => {
    if (disabled) return;
    if (!open) {
      if (e.key === "ArrowDown" || e.key === "Enter" || e.key === " ") {
        e.preventDefault();
        openMenu();
      }
      return;
    }
    if (e.key === "ArrowDown") {
      e.preventDefault();
      setActive((a) => (a + 1) % options.length);
    } else if (e.key === "ArrowUp") {
      e.preventDefault();
      setActive((a) => (a - 1 + options.length) % options.length);
    } else if (e.key === "Enter" || e.key === " ") {
      e.preventDefault();
      choose(active);
    } else if (e.key === "Escape") {
      e.preventDefault();
      // Stop the drawer's window-level Escape handler from also firing and closing the whole card.
      e.stopPropagation();
      setOpen(false);
    } else if (e.key === "Tab") {
      setOpen(false);
    }
  };

  return (
    <div className={`dropdown${className ? ` ${className}` : ""}`} ref={rootRef}>
      <button
        type="button"
        ref={triggerRef}
        className="dropdown-trigger"
        aria-haspopup="listbox"
        aria-expanded={open}
        aria-label={ariaLabel}
        disabled={disabled}
        onClick={() => (open ? setOpen(false) : openMenu())}
        onKeyDown={onKeyDown}
      >
        <span className={`dropdown-value${selected ? "" : " placeholder"}`}>
          {selected?.icon}
          <span className="truncate">{selected ? selected.label : placeholder}</span>
        </span>
        <ChevronDown size={16} className="dropdown-caret" />
      </button>

      {open && (
        <div className="dropdown-panel" role="listbox" ref={listRef}>
          {options.map((o, i) => (
            <div
              key={o.value}
              role="option"
              aria-selected={o.value === value}
              className={`dropdown-option${i === active ? " active" : ""}`}
              onMouseEnter={() => setActive(i)}
              // mousedown, not click: fire before the outside-click listener and before the
              // trigger loses focus, so the pick lands and the panel closes in one gesture.
              onMouseDown={(e) => {
                e.preventDefault();
                choose(i);
              }}
            >
              {o.icon}
              <span className="truncate">{o.label}</span>
            </div>
          ))}
        </div>
      )}
    </div>
  );
}
