import { useState } from "react";
import { Link } from "react-router-dom";
import { Archive, ArchiveRestore, LogOut, Plus, Trash2 } from "lucide-react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { archiveBoard, boardKeys, createBoard, deleteBoard, listBoards, unarchiveBoard } from "../api/boards";
import { useAuth } from "../auth/useAuth";
import { canAdmin } from "../types";
import type { Board } from "../types";
import { Avatar } from "../components/Avatar";
import { colorFor } from "../lib/color";
import { ConfirmDialog } from "../components/ConfirmDialog";
import { NewBoardDialog } from "../components/NewBoardDialog";
import { ThemeToggle } from "../components/ThemeToggle";

export function BoardsPage() {
  const { user, logout } = useAuth();
  const queryClient = useQueryClient();

  const [creating, setCreating] = useState(false);
  const [pendingDelete, setPendingDelete] = useState<Board | null>(null);

  const boards = useQuery({ queryKey: boardKeys.all, queryFn: listBoards });

  const invalidate = () => queryClient.invalidateQueries({ queryKey: boardKeys.all });

  const create = useMutation({
    mutationFn: (v: { name: string; template: string | null }) => createBoard(v.name, v.template),
    onSuccess: () => {
      setCreating(false);
      void invalidate();
    },
  });

  const archive = useMutation({
    mutationFn: (board: Board) => (board.archived ? unarchiveBoard(board.id) : archiveBoard(board.id)),
    onSuccess: invalidate,
  });

  const remove = useMutation({
    mutationFn: (boardId: string) => deleteBoard(boardId),
    onSuccess: () => {
      setPendingDelete(null);
      void invalidate();
    },
  });

  return (
    <div className="page">
      <header className="topbar">
        <div className="wordmark">
          <span className="dot" />
          Yanban
        </div>
        <div className="spacer" />
        <div className="tools">
          <ThemeToggle />
          {user && <Avatar email={user.email} name={user.displayName} />}
          <button className="icon-btn" aria-label="Log out" title="Log out" onClick={() => void logout()}>
            <LogOut size={17} />
          </button>
        </div>
      </header>

      <main className="boards">
        <div className="boards-head">
          <h1>Your boards</h1>
          <span className="muted">{boards.data?.length ?? 0} total</span>
        </div>

        {boards.isLoading && <p className="muted">Loading boards…</p>}
        {boards.isError && <p className="error">{(boards.error as Error).message}</p>}

        <div className="bento">
          {/* The newest board gets the big cell — it is the one you almost always want. */}
          {boards.data?.map((board, i) => (
            <Link
              key={board.id}
              to={`/boards/${board.id}`}
              className={[
                "tile",
                i === 0 ? "featured" : "",
                board.archived ? "archived" : "",
              ].filter(Boolean).join(" ")}
            >
              <span className="tile-art" style={{ background: colorFor(board.id) }} />
              <span className="tile-name">{board.name}</span>
              <span className="faint">
                Created {new Date(board.createdAt).toLocaleDateString(undefined, {
                  month: "short", day: "numeric", year: "numeric",
                })}
              </span>
              <span className="tile-meta">
                <span className="badge">{board.role}</span>
                {board.archived && <span className="badge">Archived</span>}
              </span>

              {/* Only an Admin may archive or delete — the API enforces it; this just avoids
                  offering a button that would 403. */}
              {canAdmin(board.role) && (
                <span className="tile-actions">
                  <button
                    className="icon-btn"
                    aria-label={board.archived ? `Unarchive ${board.name}` : `Archive ${board.name}`}
                    title={board.archived ? "Unarchive" : "Archive"}
                    onClick={(e) => {
                      // The tile is a link; these are not.
                      e.preventDefault();
                      archive.mutate(board);
                    }}
                  >
                    {board.archived ? <ArchiveRestore size={14} /> : <Archive size={14} />}
                  </button>
                  <button
                    className="icon-btn danger"
                    aria-label={`Delete ${board.name}`}
                    title="Delete"
                    onClick={(e) => {
                      e.preventDefault();
                      setPendingDelete(board);
                    }}
                  >
                    <Trash2 size={14} />
                  </button>
                </span>
              )}
            </Link>
          ))}

          <div className="tile ghost">
            <button className="ghost-cta" onClick={() => setCreating(true)}>
              <Plus size={18} />
              New board
            </button>
          </div>
        </div>

        {boards.data?.length === 0 && (
          <p className="empty">No boards yet — the tile above makes one.</p>
        )}
      </main>

      {creating && (
        <NewBoardDialog
          pending={create.isPending}
          error={create.isError ? (create.error as Error).message : null}
          onCreate={(name, template) => create.mutate({ name, template })}
          onCancel={() => { create.reset(); setCreating(false); }}
        />
      )}

      {pendingDelete && (
        <ConfirmDialog
          title={`Delete “${pendingDelete.name}”?`}
          body="Every list, card, comment and attachment on this board will be deleted. This cannot be undone."
          confirmLabel="Delete board"
          pending={remove.isPending}
          onConfirm={() => remove.mutate(pendingDelete.id)}
          onCancel={() => setPendingDelete(null)}
        />
      )}
    </div>
  );
}
