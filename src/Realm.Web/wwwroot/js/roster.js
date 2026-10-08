// Settings, "Who's on the map" (01 section 7.9): the two small things a Blazor row cannot do for itself. A drag needs dataTransfer filled in at dragstart (Firefox does not start a drag
// without it) and a drop needs the default of dragover cancelled, and both happen far too often to be sent to the server. The row's own @ondragstart and @ondrop do the real work.
// realmRoster.focus(id) moves the focus to an element by id (after a menu opens or closes, a row moves or an editor closes).
(() => {
  const ROW = '[data-roster-entity]';
  const DROP = '[data-roster-drop]';

  document.addEventListener('dragstart', (event) => {
    const target = event.target;
    if (!(target instanceof Element) || !event.dataTransfer) {
      return;
    }

    const row = target.closest(ROW);
    if (!row) {
      return;
    }

    event.dataTransfer.setData('text/plain', row.getAttribute('data-roster-entity') ?? '');
    event.dataTransfer.effectAllowed = 'move';
  });

  document.addEventListener('dragover', (event) => {
    const target = event.target;
    if (target instanceof Element && target.closest(DROP)) {
      event.preventDefault();
      if (event.dataTransfer) {
        event.dataTransfer.dropEffect = 'move';
      }
    }
  });

  // Esc inside the ⋮ menu or the editor closes that and nothing else. MudBlazor's dialog listens for Escape natively on its own element, below any handler Blazor could run, so the
  // key is taken here, before it travels: the button that closes the menu (or Cancel) is clicked, the way the keyboard user would, and the event goes no further.
  document.addEventListener('keydown', (event) => {
    const target = event.target;
    if (event.key !== 'Escape' || !(target instanceof Element)) {
      return;
    }

    const menu = target.closest('.realm-roster__menu');
    const edit = menu ? null : target.closest('.realm-roster__edit');
    const item = (menu ?? edit)?.closest('.realm-roster__item');
    if (!item) {
      return;
    }

    event.stopPropagation();
    event.preventDefault();
    if (menu) {
      const more = item.querySelector('.realm-roster__more');
      if (more instanceof HTMLElement) {
        more.click();
        more.focus();
      }
    } else {
      const cancel = item.querySelector('[data-testid="roster-cancel"]');
      if (cancel instanceof HTMLElement) {
        cancel.click();
      }
    }
  }, true);

  /** @type {any} */ (window).realmRoster = {
    /** @param {string} id */
    focus(id) {
      const element = document.getElementById(id);
      if (element instanceof HTMLElement) {
        element.focus();
      }
    },
  };
})();
