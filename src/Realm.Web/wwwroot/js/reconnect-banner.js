// Drives the static banner of App.razor (id components-reconnect-modal, 03 section 3.9). Blazor raises components-reconnect-state-changed
// on that element; this script maps each state to the banner text. It exists before the circuit does, so it needs nothing from .NET.
(() => {
  const banner = document.getElementById('components-reconnect-modal');
  if (!banner) {
    return;
  }

  const text = banner.querySelector('.realm-reconnect-text');
  const retry = banner.querySelector('.realm-reconnect-retry');
  if (!text || !retry) {
    return;
  }

  const RELOAD_KEY = 'realm.reconnect.reloadedAt';
  const RELOAD_GAP_MS = 10000;

  const show = (message, canRetry) => {
    text.textContent = message;
    retry.hidden = !canRetry;
    banner.hidden = false;
  };

  const reconnecting = () => show('Reconnecting to the court…', false);
  const outOfReach = () => show('The court is out of reach.', true);

  // A rejected or unresumable circuit needs a fresh page. At most one reload per 10 s, so a server that keeps rejecting cannot loop the page.
  const reloadOnce = () => {
    let last = 0;
    try {
      last = Number(sessionStorage.getItem(RELOAD_KEY)) || 0;
    } catch {
      last = 0;
    }

    if (Date.now() - last < RELOAD_GAP_MS) {
      outOfReach();
      return;
    }

    try {
      sessionStorage.setItem(RELOAD_KEY, String(Date.now()));
    } catch {
      // Storage is blocked: reload anyway; the guard only protects against a tight loop.
    }

    location.reload();
  };

  banner.addEventListener('components-reconnect-state-changed', (event) => {
    const state = event instanceof CustomEvent && event.detail ? event.detail.state : undefined;
    switch (state) {
      case 'show':
      case 'retrying':
        reconnecting();
        break;
      case 'failed':
        outOfReach();
        break;
      case 'resume-failed':
      case 'rejected':
        reloadOnce();
        break;
      case 'hide':
        banner.hidden = true;
        break;
      default:
        break;
    }
  });

  retry.addEventListener('click', () => {
    reconnecting();
    Blazor.reconnect().then(
      (reconnected) => {
        if (!reconnected) {
          reloadOnce();
        }
      },
      () => outOfReach(),
    );
  });
})();
