// @ts-check
// fanout.js: the pin fan-out (01 section 4.8, 03 section 4.6). Pure: no DOM, no MapLibre; Node tests import this file directly.
//
// Pins never merge into counts. When two anchors are less than 36 px apart, the higher-priority marker stays on its true point and
// every other one shifts 48 px to the right (another 48 px for each further marker). v1 draws NO leader line from a shifted marker back
// to its true point (D35, C-10): the result carries the shift and nothing else.

/**
 * A marker's true anchor in container px. `priority` decides who stays put: see {@link FAN_PRIORITY}.
 * @typedef {{ id: string, x: number, y: number, priority: number }} FanItem
 */
/**
 * The shift of one marker: `dx` px to the right, never any vertical shift, `fanned` is `dx > 0`.
 * @typedef {{ id: string, dx: number, dy: 0, fanned: boolean }} FanResult
 */

/** 01 section 4.8: anchors closer than this overlap (36 px or more apart need no fan-out); each further marker shifts another `step` px. */
export const FAN_DEFAULTS = Object.freeze({ threshold: 36, step: 48 });

/** Draw priority (01 section 4.8: selected, then members with driving first, then vehicles). */
export const FAN_PRIORITY = Object.freeze({ selected: 1000, drivingMember: 300, member: 200, vehicle: 100 });

/**
 * Items are processed in descending priority (ties by id). The group of an item is the already-processed items whose TRUE anchors are
 * within `threshold` px of this item's true anchor; its `dx` is `step * group.length`, so the second marker is 48 px to the right and
 * the third 96 px. Recompute on every frame: the shift disappears once the true points are `threshold` px or more apart.
 * The results come back in the order of `items`; `items` is not mutated.
 * @param {FanItem[]} items
 * @param {{ threshold?: number, step?: number }} [opts]
 * @returns {FanResult[]}
 */
export function fanOut(items, opts = {}) {
  const threshold = opts.threshold ?? FAN_DEFAULTS.threshold;
  const step = opts.step ?? FAN_DEFAULTS.step;
  const order = items
    .map((_, index) => index)
    .sort((a, b) => items[b].priority - items[a].priority || (items[a].id < items[b].id ? -1 : items[a].id > items[b].id ? 1 : 0));
  /** @type {number[]} */
  const dx = items.map(() => 0);
  /** @type {number[]} */
  const done = [];
  for (const index of order) {
    const item = items[index];
    const group = done.filter((other) => Math.hypot(items[other].x - item.x, items[other].y - item.y) < threshold);
    dx[index] = step * group.length;
    done.push(index);
  }
  /** @type {FanResult[]} */
  const results = [];
  items.forEach((item, index) => results.push({ id: item.id, dx: dx[index], dy: 0, fanned: dx[index] > 0 }));
  return results;
}
