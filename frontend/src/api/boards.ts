import { apiFetch } from "../lib/apiClient";
import type { Board, BoardMember, BoardRole } from "../types";

export const boardKeys = {
  all: ["boards"] as const,
  one: (boardId: string) => ["boards", boardId] as const,
  members: (boardId: string) => ["boards", boardId, "members"] as const,
};

export const listBoards = (): Promise<Board[]> => apiFetch<Board[]>("/boards");

export const getBoard = (boardId: string): Promise<Board> => apiFetch<Board>(`/boards/${boardId}`);

/**
 * `seedDefaultLists` asks the server for the starter template (Backlog / To Do / Doing / Done).
 * The lists are created in the same transaction as the board, so this cannot half-succeed the way
 * four follow-up POSTs from here could.
 */
export const createBoard = (name: string, seedDefaultLists = false): Promise<Board> =>
  apiFetch<Board>("/boards", { method: "POST", body: { name, seedDefaultLists } });

export const renameBoard = (boardId: string, name: string): Promise<Board> =>
  apiFetch<Board>(`/boards/${boardId}`, { method: "PUT", body: { name } });

/** Archiving makes a board read-only server-side; the UI mirrors that by hiding the write affordances. */
export const archiveBoard = (boardId: string): Promise<Board> =>
  apiFetch<Board>(`/boards/${boardId}/archive`, { method: "POST" });

export const unarchiveBoard = (boardId: string): Promise<Board> =>
  apiFetch<Board>(`/boards/${boardId}/unarchive`, { method: "POST" });

export const deleteBoard = (boardId: string): Promise<void> =>
  apiFetch<void>(`/boards/${boardId}`, { method: "DELETE" });

export const listMembers = (boardId: string): Promise<BoardMember[]> =>
  apiFetch<BoardMember[]>(`/boards/${boardId}/members`);

export const addMember = (boardId: string, email: string, role: BoardRole): Promise<BoardMember> =>
  apiFetch<BoardMember>(`/boards/${boardId}/members`, { method: "POST", body: { email, role } });

export const updateMember = (boardId: string, userId: string, role: BoardRole): Promise<BoardMember> =>
  apiFetch<BoardMember>(`/boards/${boardId}/members/${userId}`, { method: "PUT", body: { role } });

export const removeMember = (boardId: string, userId: string): Promise<void> =>
  apiFetch<void>(`/boards/${boardId}/members/${userId}`, { method: "DELETE" });
