/**
 * Deterministic colour from a string. Nothing is stored and every client computes the same
 * answer, so a person shows up in the same colour on every machine.
 */

/**
 * Ten hand-picked hues, all dark enough that white text on them clears WCAG AA and all still
 * legible against a dark surface. We use a fixed list rather than hsl(hash % 360, ...) because
 * a raw hue wheel passes through yellows and greens that white text can't sit on.
 *
 * One palette for both themes on purpose: colour is how you recognise someone at a glance, so
 * it shouldn't change when the theme flips.
 */
const PALETTE = [
  "#b23c30", // brick
  "#b3542a", // terracotta
  "#96620f", // amber
  "#4f7a2a", // olive
  "#1f7a5e", // teal
  "#20718f", // ocean
  "#3a63b8", // blue
  "#5a4fb0", // violet
  "#8a4296", // purple
  "#a63c6e", // magenta
];

/**
 * FNV-1a. Not cryptography, just mixing: it has to spread well enough that two colleagues at
 * the same company whose emails differ by one character don't land on the same colour.
 */
function hash(value: string): number {
  let h = 2166136261;
  for (let i = 0; i < value.length; i++) {
    h ^= value.charCodeAt(i);
    h = Math.imul(h, 16777619);
  }
  return h >>> 0;
}

/** Stable colour for any key: an email for people, a board id for board tiles. */
export const colorFor = (key: string): string =>
  PALETTE[hash(key.trim().toLowerCase()) % PALETTE.length];
