// Mirrors of the API's DTOs. Enums cross the wire as names (the API registers
// JsonStringEnumConverter), so this side speaks "Admin", not 2.

export interface AccessTokenResponse {
  accessToken: string;
  accessTokenExpiresAt: string;
}

/** `emailConfirmed` drives the nag banner. It is a prompt, never a gate: an unconfirmed
 *  account is fully usable. */
export interface User {
  id: string;
  email: string;
  displayName: string;
  emailConfirmed: boolean;
}

export type BoardRole = "Viewer" | "Editor" | "Admin";

export interface Board {
  id: string;
  name: string;
  ownerId: string;
  archived: boolean;
  createdAt: string;
  /** The calling user's role on this board, which the UI gates on. */
  role: BoardRole;
}

export interface BoardMember {
  userId: string;
  email: string;
  displayName: string;
  role: BoardRole;
}

export interface BoardList {
  id: string;
  boardId: string;
  name: string;
  rank: string;
}

export interface Card {
  id: string;
  listId: string;
  title: string;
  description: string | null;
  dueDate: string | null;
  rank: string;
  /** Postgres xmin. Echoed back as If-Match to update the card. */
  version: number;
  assigneeId: string | null;
  createdById: string;
}

export interface Comment {
  id: string;
  cardId: string;
  authorId: string;
  authorDisplayName: string;
  body: string;
  createdAt: string;
  editedAt: string | null;
}

export interface Attachment {
  id: string;
  cardId: string;
  fileName: string;
  contentType: string;
  sizeBytes: number;
  uploadedById: string;
  createdAt: string;
}

export interface UploadTicket {
  attachmentId: string;
  method: string;
  uploadUrl: string;
  contentType: string;
  expiresAt: string;
}

export interface DownloadUrl {
  downloadUrl: string;
  expiresAt: string;
}

export interface CardSearchHit {
  id: string;
  listId: string;
  listName: string;
  title: string;
  description: string | null;
  dueDate: string | null;
  assigneeId: string | null;
}

export interface CardTemplate {
  id: string;
  boardId: string;
  name: string;
  title: string;
  description: string | null;
  createdAt: string;
}

/** What the server logs and what it pushes over the hub are the same shape. */
export interface Activity {
  sequence: number;
  boardId: string;
  actorId: string;
  actorDisplayName: string;
  action: string;
  entityType: string;
  entityId: string;
  summary: string | null;
  /** Set for renames, null for everything else: a creation has no "before". */
  oldValue: string | null;
  newValue: string | null;
  createdAt: string;
}

/** A starter layout the new-board dialog can offer. `id` is what create sends back. */
export interface BoardTemplate {
  id: string;
  name: string;
  lists: string[];
}

/** What a board is holding, against what it may hold. `usedBytes` counts completed files only. */
export interface BoardUsage {
  usedBytes: number;
  maxBoardBytes: number;
  maxFileBytes: number;
  fileCount: number;
}

export type NotificationType =
  | "CardAssigned"
  | "CardUnassigned"
  | "AssignedCardMoved"
  | "CommentCreated";

export interface NotificationPreference {
  type: NotificationType;
  enabled: boolean;
}

/** Every field optional and they compose; none of them is the plain newest-first feed. */
export interface ActivityFilters {
  q?: string;
  actorId?: string;
  action?: string;
  entityType?: string;
}

/** Ascending order of power, for role pickers. */
export const BOARD_ROLES: BoardRole[] = ["Viewer", "Editor", "Admin"];

// The UI hides what the API would 403 anyway. These are a courtesy, not a control:
// the server re-decides every one of them.
export const canWrite = (role: BoardRole): boolean => role === "Editor" || role === "Admin";
export const canAdmin = (role: BoardRole): boolean => role === "Admin";
