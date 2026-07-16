/**
 * A due date is a calendar day, not an instant.
 *
 * CardDetail writes it as UTC midnight (`${yyyy-mm-dd}T00:00:00Z`), so the day it means is its UTC
 * day, and everything reading it back has to agree. Read it in local time and a browser west of
 * Greenwich would render, and flag, the day before.
 */

/** Days since the epoch, in UTC. Comparing these compares calendar days and nothing else. */
const dayNumber = (d: Date): number =>
  Math.floor(Date.UTC(d.getUTCFullYear(), d.getUTCMonth(), d.getUTCDate()) / 86_400_000);

/**
 * A card is overdue once its due day is behind us; a card due today is due, not late.
 *
 * The obvious `new Date(dueDate) < new Date()` is wrong for that reason: the stored instant
 * is midnight, so a card due today counts as "in the past" from 00:00:01 onwards and would show
 * an Overdue flag all day.
 */
export const isOverdue = (dueDate: string | null | undefined, now: Date = new Date()): boolean =>
  dueDate ? dayNumber(new Date(dueDate)) < dayNumber(now) : false;

/** Renders the stored UTC day in the viewer's locale but not their timezone; see above. */
export const formatDue = (dueDate: string): string =>
  new Date(dueDate).toLocaleDateString(undefined, { month: "short", day: "numeric", timeZone: "UTC" });
