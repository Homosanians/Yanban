import { useState } from "react";
import type { FormEvent } from "react";
import { UserMinus, X } from "lucide-react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { addMember, boardKeys, listMembers, removeMember, updateMember } from "../api/boards";
import { BOARD_ROLES, canAdmin } from "../types";
import type { BoardMember, BoardRole } from "../types";
import { Avatar } from "./Avatar";
import { ConfirmDialog } from "./ConfirmDialog";

interface Props {
  boardId: string;
  role: BoardRole;
  onClose: () => void;
}

export function MembersPanel({ boardId, role, onClose }: Props) {
  const queryClient = useQueryClient();
  const [email, setEmail] = useState("");
  const [newRole, setNewRole] = useState<BoardRole>("Editor");
  const [pendingRemove, setPendingRemove] = useState<BoardMember | null>(null);

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
    onSuccess: () => {
      setPendingRemove(null);
      void invalidate();
    },
  });

  const onAdd = (e: FormEvent) => {
    e.preventDefault();
    if (email.trim()) add.mutate();
  };

  return (
    <aside className="panel">
      <header className="panel-head">
        <h2>Members</h2>
        <button className="icon-btn" aria-label="Close members" title="Close" onClick={onClose}>
          <X size={16} />
        </button>
      </header>

      <div className="panel-body">
        {admin && (
          <form className="stack" onSubmit={onAdd}>
            <input
              type="email"
              value={email}
              onChange={(e) => setEmail(e.target.value)}
              placeholder="Invite by email"
            />
            <div className="row">
              <select
                className="grow"
                value={newRole}
                onChange={(e) => setNewRole(e.target.value as BoardRole)}
              >
                {BOARD_ROLES.map((r) => (
                  <option key={r} value={r}>{r}</option>
                ))}
              </select>
              <button type="submit" disabled={add.isPending || !email.trim()}>Invite</button>
            </div>
            {add.isError && <p className="error">{(add.error as Error).message}</p>}
          </form>
        )}

        <ul className="plain">
          {members.data?.map((m) => (
            <li key={m.userId} className="member">
              <Avatar email={m.email} name={m.displayName} />

              {/* min-width:0 (via .who) is what lets a long email ellipsize instead of shoving
                  the role select out of the panel. */}
              <div className="who">
                <strong>{m.displayName}</strong>
                <span className="email" title={m.email}>{m.email}</span>
              </div>

              {admin ? (
                <div className="controls">
                  <select
                    value={m.role}
                    aria-label={`Role for ${m.displayName}`}
                    onChange={(e) => change.mutate({ userId: m.userId, role: e.target.value as BoardRole })}
                  >
                    {BOARD_ROLES.map((r) => (
                      <option key={r} value={r}>{r}</option>
                    ))}
                  </select>
                  <button
                    className="icon-btn danger"
                    aria-label={`Remove ${m.displayName}`}
                    title="Remove from board"
                    onClick={() => setPendingRemove(m)}
                  >
                    <UserMinus size={15} />
                  </button>
                </div>
              ) : (
                <span className="badge">{m.role}</span>
              )}
            </li>
          ))}
        </ul>

        {(change.isError || remove.isError) && (
          <p className="error">{((change.error ?? remove.error) as Error).message}</p>
        )}
      </div>

      {pendingRemove && (
        <ConfirmDialog
          title={`Remove ${pendingRemove.displayName}?`}
          body={`${pendingRemove.email} will lose access to this board immediately, and any card assigned to them will be unassigned.`}
          confirmLabel="Remove member"
          pending={remove.isPending}
          onConfirm={() => remove.mutate(pendingRemove.userId)}
          onCancel={() => setPendingRemove(null)}
        />
      )}
    </aside>
  );
}
