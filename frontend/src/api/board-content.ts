import { apiFetch } from "../lib/apiClient";
import type {
  Activity,
  ActivityFilters,
  Attachment,
  Card,
  CardSearchHit,
  CardTemplate,
  Comment,
  BoardList,
  DownloadUrl,
  UploadTicket,
} from "../types";

export const contentKeys = {
  lists: (boardId: string) => ["boards", boardId, "lists"] as const,
  cards: (boardId: string, listId: string) => ["boards", boardId, "lists", listId, "cards"] as const,
  card: (boardId: string, cardId: string) => ["boards", boardId, "cards", cardId] as const,
  comments: (boardId: string, cardId: string) => ["boards", boardId, "cards", cardId, "comments"] as const,
  attachments: (boardId: string, cardId: string) => ["boards", boardId, "cards", cardId, "attachments"] as const,
  templates: (boardId: string) => ["boards", boardId, "templates"] as const,
  activity: (boardId: string) => ["boards", boardId, "activity"] as const,
  search: (boardId: string, q: string) => ["boards", boardId, "search", q] as const,
};

// --- lists ---

export const listLists = (boardId: string): Promise<BoardList[]> =>
  apiFetch<BoardList[]>(`/boards/${boardId}/lists`);

export const createList = (boardId: string, name: string): Promise<BoardList> =>
  apiFetch<BoardList>(`/boards/${boardId}/lists`, { method: "POST", body: { name } });

export const renameList = (boardId: string, listId: string, name: string): Promise<BoardList> =>
  apiFetch<BoardList>(`/boards/${boardId}/lists/${listId}`, { method: "PUT", body: { name } });

export const deleteList = (boardId: string, listId: string): Promise<void> =>
  apiFetch<void>(`/boards/${boardId}/lists/${listId}`, { method: "DELETE" });

// --- cards ---

export const listCards = (boardId: string, listId: string): Promise<Card[]> =>
  apiFetch<Card[]>(`/boards/${boardId}/lists/${listId}/cards`);

export const getCard = (boardId: string, cardId: string): Promise<Card> =>
  apiFetch<Card>(`/boards/${boardId}/cards/${cardId}`);

export const createCard = (
  boardId: string,
  listId: string,
  title: string,
  description?: string | null,
): Promise<Card> =>
  apiFetch<Card>(`/boards/${boardId}/lists/${listId}/cards`, {
    method: "POST",
    body: { title, description: description ?? null, dueDate: null },
  });

export const createCardFromTemplate = (
  boardId: string,
  listId: string,
  templateId: string,
  title?: string,
): Promise<Card> =>
  apiFetch<Card>(`/boards/${boardId}/lists/${listId}/cards/from-template`, {
    method: "POST",
    body: { templateId, title: title ?? null },
  });

/**
 * The only mutation that carries optimistic concurrency. `version` is the card's xmin as the
 * server last reported it; If-Match makes the write conditional on the row still being at that
 * version, so a concurrent edit loses with a 412 instead of silently overwriting.
 */
export const updateCard = (
  boardId: string,
  cardId: string,
  version: number,
  fields: { title: string; description: string | null; dueDate: string | null },
): Promise<Card> =>
  apiFetch<Card>(`/boards/${boardId}/cards/${cardId}`, {
    method: "PUT",
    body: fields,
    headers: { "If-Match": `"${version}"` },
  });

/**
 * Position is a 0-based index within the target list; the server turns it into a LexoRank.
 * No If-Match: the API does not demand one here, and a reordering is not the kind of change
 * that can silently clobber someone's text.
 */
export const moveCard = (
  boardId: string,
  cardId: string,
  targetListId: string,
  position: number,
): Promise<Card> =>
  apiFetch<Card>(`/boards/${boardId}/cards/${cardId}/move`, {
    method: "POST",
    body: { targetListId, position },
  });

export const assignCard = (boardId: string, cardId: string, assigneeId: string | null): Promise<Card> =>
  apiFetch<Card>(`/boards/${boardId}/cards/${cardId}/assignee`, { method: "PUT", body: { assigneeId } });

export const deleteCard = (boardId: string, cardId: string): Promise<void> =>
  apiFetch<void>(`/boards/${boardId}/cards/${cardId}`, { method: "DELETE" });

// --- comments ---

export const listComments = (boardId: string, cardId: string): Promise<Comment[]> =>
  apiFetch<Comment[]>(`/boards/${boardId}/cards/${cardId}/comments`);

export const createComment = (boardId: string, cardId: string, body: string): Promise<Comment> =>
  apiFetch<Comment>(`/boards/${boardId}/cards/${cardId}/comments`, { method: "POST", body: { body } });

export const updateComment = (
  boardId: string,
  cardId: string,
  commentId: string,
  body: string,
): Promise<Comment> =>
  apiFetch<Comment>(`/boards/${boardId}/cards/${cardId}/comments/${commentId}`, {
    method: "PUT",
    body: { body },
  });

export const deleteComment = (boardId: string, cardId: string, commentId: string): Promise<void> =>
  apiFetch<void>(`/boards/${boardId}/cards/${cardId}/comments/${commentId}`, { method: "DELETE" });

// --- attachments ---

export const listAttachments = (boardId: string, cardId: string): Promise<Attachment[]> =>
  apiFetch<Attachment[]>(`/boards/${boardId}/cards/${cardId}/attachments`);

/**
 * Uploads never pass through the API: it hands out a presigned URL and the browser PUTs the
 * bytes straight to object storage, then tells the API the upload landed. The Content-Type
 * must match the ticket exactly, since it is a signed header, so anything else is rejected
 * by storage as a bad signature.
 */
export async function uploadAttachment(boardId: string, cardId: string, file: File): Promise<Attachment> {
  const ticket = await apiFetch<UploadTicket>(`/boards/${boardId}/cards/${cardId}/attachments`, {
    method: "POST",
    body: {
      fileName: file.name,
      contentType: file.type || "application/octet-stream",
      sizeBytes: file.size,
    },
  });

  const put = await fetch(ticket.uploadUrl, {
    method: ticket.method,
    headers: { "Content-Type": ticket.contentType },
    body: file,
  });
  if (!put.ok) throw new Error(`Upload to storage failed (${put.status}).`);

  // Only now is the attachment real: the API HEADs the object and records its true size.
  return apiFetch<Attachment>(
    `/boards/${boardId}/cards/${cardId}/attachments/${ticket.attachmentId}/complete`,
    { method: "POST" },
  );
}

export const getDownloadUrl = (boardId: string, cardId: string, attachmentId: string): Promise<DownloadUrl> =>
  apiFetch<DownloadUrl>(`/boards/${boardId}/cards/${cardId}/attachments/${attachmentId}/download`);

export const deleteAttachment = (boardId: string, cardId: string, attachmentId: string): Promise<void> =>
  apiFetch<void>(`/boards/${boardId}/cards/${cardId}/attachments/${attachmentId}`, { method: "DELETE" });

// --- search ---

export const searchCards = (boardId: string, q: string): Promise<CardSearchHit[]> =>
  apiFetch<CardSearchHit[]>(`/boards/${boardId}/cards/search?q=${encodeURIComponent(q)}`);

// --- templates ---

export const listTemplates = (boardId: string): Promise<CardTemplate[]> =>
  apiFetch<CardTemplate[]>(`/boards/${boardId}/templates`);

export const createTemplate = (
  boardId: string,
  fields: { name: string; title: string; description: string | null },
): Promise<CardTemplate> =>
  apiFetch<CardTemplate>(`/boards/${boardId}/templates`, { method: "POST", body: fields });

export const deleteTemplate = (boardId: string, templateId: string): Promise<void> =>
  apiFetch<void>(`/boards/${boardId}/templates/${templateId}`, { method: "DELETE" });

// --- activity ---

/**
 * Newest-first, paged by a `before` sequence cursor (keyset, not offset).
 *
 * The filters compose with the cursor: paging deeper into a search works exactly like paging deeper
 * into the plain feed, which is the whole reason the cursor is a sequence and not an offset.
 */
export const listActivity = (
  boardId: string,
  before?: number,
  filters: ActivityFilters = {},
): Promise<Activity[]> => {
  const params = new URLSearchParams({ limit: "20" });
  if (before !== undefined) params.set("before", String(before));
  if (filters.q) params.set("q", filters.q);
  if (filters.actorId) params.set("actorId", filters.actorId);
  if (filters.action) params.set("action", filters.action);
  if (filters.entityType) params.set("entityType", filters.entityType);

  return apiFetch<Activity[]>(`/boards/${boardId}/activity?${params}`);
};
