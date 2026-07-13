import { useState } from "react";
import type { FormEvent } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { addMember, boardKeys, listMembers, removeMember, updateMember } from "../api/boards";
import { BOARD_ROLES, canAdmin } from "../types";
import type { BoardRole } from "../types";

interface Props {
  boardId: string;
  role: BoardRole;
  onClose: () => void;
}

export function MembersPanel({ boardId, role, onClose }: Props) {
  const queryClient = useQueryClient();
  const [email, setEmail] = useState("");
  const [newRole, setNewRole] = useState<BoardRole>("Editor");

  const members = useQuery({ queryKey: boardKeys.members(boardId), queryFn: () => listMembers(boardId) });
  const invalidate = () => queryClient.invalidateQueries({ queryKey: boardKeys.members(boardId) });

  // Only an Admin manages membership; everyone else sees the roster read-only.
  const admin = canAdmin(role);

  const add = useMutation({
    mutationFn: () => addMember(boardId, email.trim(), newRole),
    onSuccess: () => {
      setEmail("");
      void invalidate();
    },
  });

  const change = useMutation({
    mutationFn: (v: { userId: string; role: BoardRole }) => updateMember(boardId, v.userId, v.role),
    onSuccess: invalidate,
  });

  const remove = useMutation({
    mutationFn: (userId: string) => removeMember(boardId, userId),
    onSuccess: invalidate,
  });

  const onAdd = (e: FormEvent) => {
    e.preventDefault();
    if (email.trim()) add.mutate();
  };

  return (
    <aside className="panel">
      <header className="drawer-head">
        <h2>Members</h2>
        <button className="ghost" onClick={onClose}>Close</button>
      </header>

      {admin && (
        <form className="stack" onSubmit={onAdd}>
          <input
            type="email"
            value={email}
            onChange={(e) => setEmail(e.target.value)}
            placeholder="Invite by email"
          />
          <select value={newRole} onChange={(e) => setNewRole(e.target.value as BoardRole)}>
            {BOARD_ROLES.map((r) => (
              <option key={r} value={r}>{r}</option>
            ))}
          </select>
          <button type="submit" disabled={add.isPending || !email.trim()}>Add member</button>
          {add.isError && <p className="error">{(add.error as Error).message}</p>}
        </form>
      )}

      <ul className="plain">
        {members.data?.map((m) => (
          <li key={m.userId} className="member">
            <div>
              <strong>{m.displayName}</strong>
              <div className="muted">{m.email}</div>
            </div>
            {admin ? (
              <div className="row">
                <select
                  value={m.role}
                  onChange={(e) => change.mutate({ userId: m.userId, role: e.target.value as BoardRole })}
                >
                  {BOARD_ROLES.map((r) => (
                    <option key={r} value={r}>{r}</option>
                  ))}
                </select>
                <button className="ghost danger" onClick={() => remove.mutate(m.userId)}>Remove</button>
              </div>
            ) : (
              <span className="role">{m.role}</span>
            )}
          </li>
        ))}
      </ul>
      {(change.isError || remove.isError) && (
        <p className="error">{((change.error ?? remove.error) as Error).message}</p>
      )}
    </aside>
  );
}
