import { X } from "lucide-react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import {
  boardSettingsKeys,
  getBoardUsage,
  getNotificationPreferences,
  setNotificationPreference,
} from "../api/boards";
import { formatBytes } from "../lib/bytes";
import type { NotificationType } from "../types";

interface Props {
  boardId: string;
  onClose: () => void;
}

const LABELS: Record<NotificationType, string> = {
  CardAssigned: "A card is assigned to me",
  CardUnassigned: "A card is unassigned from me",
  AssignedCardMoved: "My card is moved",
  CommentCreated: "Someone comments on my card",
};

export function BoardSettingsPanel({ boardId, onClose }: Props) {
  const queryClient = useQueryClient();

  const usage = useQuery({
    queryKey: boardSettingsKeys.usage(boardId),
    queryFn: () => getBoardUsage(boardId),
  });

  const prefs = useQuery({
    queryKey: boardSettingsKeys.notifications(boardId),
    queryFn: () => getNotificationPreferences(boardId),
  });

  const toggle = useMutation({
    mutationFn: (v: { type: NotificationType; enabled: boolean }) =>
      setNotificationPreference(boardId, v.type, v.enabled),
    onSuccess: () =>
      void queryClient.invalidateQueries({ queryKey: boardSettingsKeys.notifications(boardId) }),
  });

  const used = usage.data?.usedBytes ?? 0;
  const cap = usage.data?.maxBoardBytes ?? 0;
  const percent = cap > 0 ? Math.min(100, (used / cap) * 100) : 0;

  return (
    <aside className="panel">
      <header className="panel-head">
        <h2>Board settings</h2>
        <button className="icon-btn" aria-label="Close settings" title="Close" onClick={onClose}>
          <X size={16} />
        </button>
      </header>

      <div className="panel-body">
        <section className="stack">
          <h3>Storage</h3>

          {usage.isLoading && <p className="muted">Loading…</p>}
          {usage.isError && <p className="error">{(usage.error as Error).message}</p>}

          {usage.data && (
            <>
              <p className="usage-figure">
                <strong>{formatBytes(used)}</strong> of {formatBytes(cap)} used
              </p>

              {/* A real <progress>: it is a meter, and the platform already has one that screen
                  readers announce correctly. `percent` is capped at 100 so a quota lowered beneath
                  what a board already holds cannot overflow the bar. */}
              <progress
                className={percent >= 90 ? "meter full" : "meter"}
                value={percent}
                max={100}
                aria-label="Board storage used"
              />

              <p className="faint">
                {usage.data.fileCount} {usage.data.fileCount === 1 ? "file" : "files"} ·
                {" "}Up to {formatBytes(usage.data.maxFileBytes)} per file
              </p>
            </>
          )}
        </section>

        <section className="stack">
          <h3>Notify me on this board</h3>

          {prefs.isError && <p className="error">{(prefs.error as Error).message}</p>}

          {prefs.data?.map((p) => (
            <label key={p.type} className="check">
              <input
                type="checkbox"
                checked={p.enabled}
                disabled={toggle.isPending}
                onChange={(e) => toggle.mutate({ type: p.type, enabled: e.target.checked })}
              />
              {LABELS[p.type]}
            </label>
          ))}

          <p className="faint">
            Email only, and never about your own doing — you are not told that you assigned yourself
            a card.
          </p>
        </section>
      </div>
    </aside>
  );
}
