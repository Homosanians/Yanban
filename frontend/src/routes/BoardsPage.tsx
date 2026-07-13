import { useState } from "react";
import type { FormEvent } from "react";
import { Link } from "react-router-dom";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { archiveBoard, boardKeys, createBoard, deleteBoard, listBoards, unarchiveBoard } from "../api/boards";
import { useAuth } from "../auth/useAuth";
import { canAdmin } from "../types";
import type { Board } from "../types";

export function BoardsPage() {
  const { user, logout } = useAuth();
  const queryClient = useQueryClient();
  const [name, setName] = useState("");

  const boards = useQuery({ queryKey: boardKeys.all, queryFn: listBoards });

  const invalidate = () => queryClient.invalidateQueries({ queryKey: boardKeys.all });

  const create = useMutation({
    mutationFn: (boardName: string) => createBoard(boardName),
    onSuccess: () => {
      setName("");
      void invalidate();
    },
  });

  const archive = useMutation({
    mutationFn: (board: Board) => (board.archived ? unarchiveBoard(board.id) : archiveBoard(board.id)),
    onSuccess: invalidate,
  });

  const remove = useMutation({
    mutationFn: (boardId: string) => deleteBoard(boardId),
    onSuccess: invalidate,
  });

  const onCreate = (e: FormEvent) => {
    e.preventDefault();
    if (name.trim()) create.mutate(name.trim());
  };

  return (
    <div className="page">
      <header className="topbar">
        <h1>Yanban</h1>
        <div className="user">
          <span>{user?.displayName}</span>
          <button className="ghost" onClick={() => void logout()}>Log out</button>
        </div>
      </header>

      <main className="boards">
        <form className="row" onSubmit={onCreate}>
          <input
            value={name}
            onChange={(e) => setName(e.target.value)}
            placeholder="New board name"
            maxLength={200}
          />
          <button type="submit" disabled={create.isPending || !name.trim()}>Create board</button>
        </form>
        {create.isError && <p className="error">{(create.error as Error).message}</p>}

        {boards.isLoading && <p>Loading boards...</p>}
        {boards.isError && <p className="error">{(boards.error as Error).message}</p>}

        <ul className="board-list">
          {boards.data?.map((board) => (
            <li key={board.id} className={board.archived ? "board archived" : "board"}>
              <Link to={`/boards/${board.id}`} className="board-name">
                {board.name}
                {board.archived && <span className="badge">archived</span>}
              </Link>
              <span className="role">{board.role}</span>
              {/* Only an Admin may archive or delete — the API enforces it; this just avoids
                  offering a button that would 403. */}
              {canAdmin(board.role) && (
                <span className="actions">
                  <button className="ghost" onClick={() => archive.mutate(board)}>
                    {board.archived ? "Unarchive" : "Archive"}
                  </button>
                  <button className="ghost danger" onClick={() => remove.mutate(board.id)}>Delete</button>
                </span>
              )}
            </li>
          ))}
        </ul>

        {boards.data?.length === 0 && <p className="muted">No boards yet. Create one above.</p>}
      </main>
    </div>
  );
}
