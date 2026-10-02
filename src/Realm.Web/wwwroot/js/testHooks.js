// @ts-check
// testHooks.js: installs `window.__realm` (03 section 4.10, 01 Appendix B) when init asked for test hooks (a Demo session, or
// Realm:TestHooks). The hooks are read-only observers and pure functions: none of them changes application state, and nothing
// outside the ten names below is ever installed (O-10).
//
// realmMap.js owns the state, so it passes the observers in as `probe`; this file only decides which names exist and freezes them.
// S5a provides `mapPadding`, `camera`, `pins`, `zones`, `styleId`, `settled` and `stats`; `sheet` arrives with S7a, `bubbles` and
// `layoutBubbles` with S9, by adding them to the probe (realmMap.js) and leaving this file untouched.

/** The whole v1 surface of `window.__realm`. */
export const HOOK_NAMES = Object.freeze(['mapPadding', 'camera', 'sheet', 'pins', 'bubbles', 'zones', 'styleId', 'layoutBubbles', 'settled', 'stats']);

/**
 * Installs the hooks the probe provides and returns them.
 * @param {Window} win
 * @param {Record<string, unknown>} probe the observers, by name
 * @returns {Readonly<Record<string, unknown>>}
 */
export function installTestHooks(win, probe) {
  /** @type {Record<string, unknown>} */
  const hooks = {};
  for (const name of HOOK_NAMES) {
    if (typeof probe[name] === 'function') hooks[name] = probe[name];
  }
  const frozen = Object.freeze(hooks);
  Object.defineProperty(win, '__realm', { value: frozen, configurable: true, enumerable: true, writable: false });
  return frozen;
}

/**
 * Removes the hooks (dispose, and a repeated init that does not ask for them).
 * @param {Window} win
 */
export function removeTestHooks(win) {
  try {
    delete (/** @type {any} */ (win)).__realm;
  } catch {
    // a non-configurable property cannot be deleted; a later install replaces nothing and the stale hooks simply stay
  }
}
