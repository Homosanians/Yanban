import { useQuery } from "@tanstack/react-query";
import { Paperclip } from "lucide-react";
import { contentKeys, getDownloadUrl } from "../api/board-content";
import type { Attachment } from "../types";

// Load a preview inline only for images small enough not to be worth resizing. Bigger images and
// non-images keep the generic icon.
const THUMB_MAX_BYTES = 1_048_576;

interface Props {
  boardId: string;
  cardId: string;
  attachment: Attachment;
}

export function AttachmentThumb({ boardId, cardId, attachment }: Props) {
  const previewable =
    attachment.contentType.startsWith("image/") && attachment.sizeBytes < THUMB_MAX_BYTES;

  // The presigned URL is short-lived, so fetch it per attachment only when we will actually show
  // a thumbnail, and let it go stale after a few minutes.
  const url = useQuery({
    queryKey: [...contentKeys.attachments(boardId, cardId), attachment.id, "thumb"],
    queryFn: () => getDownloadUrl(boardId, cardId, attachment.id),
    enabled: previewable,
    staleTime: 5 * 60 * 1000,
  });

  if (previewable && url.data) {
    return <img className="attachment-thumb" src={url.data.downloadUrl} alt={attachment.fileName} loading="lazy" />;
  }
  return (
    <span className="attachment-thumb icon">
      <Paperclip size={15} />
    </span>
  );
}
