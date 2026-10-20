// What ModRouter does in the browser after a committed page rendered: the document title, keyboard focus, scroll
// (top, fragment, restore on back/forward). Everything that waits for the page (a heading rendered after an await, a
// fragment target, content that makes a restored position reachable) is bounded and abandoned as soon as it is stale:
// a newer navigation, or the user pressing a mouse button, touching the screen, turning the mouse wheel or pressing a key.

const positions = new Map();
const tracked = new WeakSet();
let current = null; // the pending work of the latest navigation

const windowKey = 'window';

function containerElement(selector) {
  return selector ? document.querySelector(selector) : null;
}

function scrollTarget(selector) {
  return selector ? containerElement(selector) : window;
}

function containerKey(selector) {
  return selector ?? windowKey;
}

// Blazor keeps a per-entry index in history.state (null for the first entry); it is stable across back/forward.
function entryKey(selector) {
  const index = history.state && typeof history.state._index === 'number' ? history.state._index : 0;
  return `${index}|${containerKey(selector)}`;
}

function readPosition(selector) {
  const el = containerElement(selector);
  return el ? { x: el.scrollLeft, y: el.scrollTop } : { x: window.scrollX, y: window.scrollY };
}

function scrollTo(selector, x, y) {
  const el = containerElement(selector);
  if (el) {
    el.scrollTo({ left: x, top: y, behavior: 'instant' });
  } else {
    window.scrollTo({ left: x, top: y, behavior: 'instant' });
  }
}

let transition = null; // the view transition whose update the router is still rendering

// Starts a view transition whose update step waits for endTransition (or the timeout). The browser keeps a picture of the
// current page until then and paints nothing, so the wait is bounded. Returns false (and the router renders as usual) when
// the browser has no API, the user prefers reduced motion, or a transition is still running.
export function beginTransition(timeoutMs) {
  if (typeof document.startViewTransition !== 'function' || transition || window.matchMedia('(prefers-reduced-motion: reduce)').matches) {
    return false;
  }

  let release;
  const updated = new Promise(resolve => { release = resolve; });
  const timer = setTimeout(release, timeoutMs);
  const own = { release: () => { clearTimeout(timer); release(); } };
  transition = own;

  const started = document.startViewTransition(() => updated);
  started.finished.finally(() => { if (transition === own) { transition = null; } });

  return true;
}

// The new page has rendered (and its scroll and focus are in place): let the browser take the picture of it.
export function endTransition() {
  transition?.release();
}

// Saves the scroll position of the current history entry while the user scrolls (throttled to one write per frame).
export function trackScroll(selector) {
  // The router restores positions itself. Left on 'auto' the browser restores its own saved position for the entry too, at a
  // moment of its choosing (after Blazor's back/forward round trip), and the two fight.
  if ('scrollRestoration' in history) {
    history.scrollRestoration = 'manual';
  }

  const target = scrollTarget(selector ?? null);
  if (!target || tracked.has(target)) {
    return;
  }

  tracked.add(target);
  let queued = false;
  target.addEventListener('scroll', () => {
    if (queued) {
      return;
    }
    queued = true;
    requestAnimationFrame(() => {
      queued = false;
      positions.set(entryKey(selector ?? null), readPosition(selector ?? null));
    });
  }, { passive: true });
}

const interactionEvents = ['wheel', 'touchstart', 'keydown', 'mousedown'];

// A unit of pending work that can be abandoned: by the next navigation, by the user, or by its own deadline.
function startWork(timeoutMs) {
  const work = {
    done: false,
    cleanups: [],
    finish() {
      if (work.done) {
        return;
      }
      work.done = true;
      work.cleanups.forEach(c => c());
      work.cleanups = [];
    },
    onCleanup(fn) {
      work.cleanups.push(fn);
    },
  };

  const abandon = () => work.finish();
  interactionEvents.forEach(name => window.addEventListener(name, abandon, { capture: true, passive: true }));
  work.onCleanup(() => interactionEvents.forEach(name => window.removeEventListener(name, abandon, { capture: true })));

  const timer = setTimeout(abandon, timeoutMs);
  work.onCleanup(() => clearTimeout(timer));

  return work;
}

// Runs check() now and after every DOM change until it returns true or the work ends. Work that is satisfied at once is
// finished at once, so its abandon listeners and deadline do not linger until the timeout.
function untilTrue(work, check) {
  if (check()) {
    work.finish();
    return;
  }

  const observer = new MutationObserver(() => {
    if (!work.done && check()) {
      work.finish();
    }
  });
  observer.observe(document.body, { childList: true, subtree: true, attributes: true });
  work.onCleanup(() => observer.disconnect());
}

function isTextEntry(el) {
  if (!el || el === document.body) {
    return false;
  }
  const tag = el.tagName;
  return tag === 'INPUT' || tag === 'TEXTAREA' || tag === 'SELECT' || el.isContentEditable === true;
}

const warnedSelectors = new Set();

// An app can configure any string as the focus selector. One the browser rejects is no match (the alert fallback still
// applies), and the console says so once per selector instead of on every navigation.
function querySelectorOrNull(selector) {
  try {
    // A page kept alive stays in the document, hidden: its heading is never the one to focus.
    for (const element of document.querySelectorAll(selector)) {
      if (!element.closest('[data-mod-keep-alive="inactive"]')) {
        return element;
      }
    }

    return null;
  } catch (error) {
    if (error?.name !== 'SyntaxError') {
      throw error;
    }

    if (!warnedSelectors.has(selector)) {
      warnedSelectors.add(selector);
      console.warn(`ModRouter: the focus selector "${selector}" is not a valid CSS selector and is ignored.`);
    }

    return null;
  }
}

// The alert fallback is limited to the router's own error content: ModRouter wraps every error it renders in an error host,
// so a danger message that belongs to a layout or a page is never chosen.
const errorAlertSelector = '[data-mod-route-error-host] [role="alert"], [data-mod-route-error-host] [data-mod-route-error]';

function focusTarget(selector) {
  return querySelectorOrNull(selector) ?? document.querySelector(errorAlertSelector);
}

function focusWhenReady(effect, work) {
  const startedWith = document.activeElement;

  // Never take focus from somebody typing.
  if (isTextEntry(startedWith)) {
    work.finish();
    return;
  }

  const tryFocus = () => {
    const active = document.activeElement;
    // The user moved focus on purpose in the meantime (the focused element was not removed, so it is not body).
    if (active && active !== document.body && active !== startedWith) {
      work.finish();
      return true;
    }
    if (isTextEntry(active)) {
      work.finish();
      return true;
    }

    const el = focusTarget(effect.selector);
    if (!el) {
      return false;
    }
    if (!el.hasAttribute('tabindex') && !el.matches('a[href], button, input, select, textarea, [contenteditable]')) {
      el.setAttribute('tabindex', '-1');
    }
    el.focus({ preventScroll: true });
    return true;
  };

  untilTrue(work, tryFocus);
}

function fragmentElement(fragment) {
  // The fragment comes from the address bar, so it can be malformed (%E0%A4). The browser matches such a fragment as
  // written, and so does the router.
  let id;
  try {
    id = decodeURIComponent(fragment);
  } catch {
    id = fragment;
  }

  return document.getElementById(id) ?? document.getElementsByName(id)[0] ?? null;
}

function scrollWhenReady(effect, work) {
  const selector = effect.container ?? null;

  if (effect.mode === 'top') {
    scrollTo(selector, 0, 0);
    work.finish();
    return;
  }

  if (effect.mode === 'fragment') {
    untilTrue(work, () => {
      const el = fragmentElement(effect.fragment);
      if (!el) {
        return false;
      }
      el.scrollIntoView({ behavior: 'instant', block: 'start' });
      return true;
    });
    return;
  }

  // restore: the saved position may not be reachable until late content has rendered, so try again on DOM changes. An
  // entry without a saved position was never scrolled (not even by the router's own scroll to the top, which only
  // fires a scroll event when it moves something): it was at the top, so that is where it goes back to.
  const saved = positions.get(entryKey(selector)) ?? { x: 0, y: 0 };

  untilTrue(work, () => {
    scrollTo(selector, saved.x, saved.y);
    const now = readPosition(selector);
    return Math.abs(now.y - saved.y) < 2 && Math.abs(now.x - saved.x) < 2;
  });
}

// effects: { title, focus: { selector, timeoutMs }, scroll: { mode, fragment, container, timeoutMs } }; any member may be null.
export function afterNavigation(effects) {
  if (current) {
    current.finish();
    current = null;
  }

  if (typeof effects.title === 'string') {
    document.title = effects.title;
  }

  // Assigned before any work starts, so the next navigation can finish all of it however this call ends.
  const works = [];
  current = { finish: () => works.forEach(w => w.finish()) };

  // Scroll and focus are independent: each has its own deadline and finishes on its own. One that throws is finished
  // (its listeners and deadline released) without canceling the other; the first failure is reported once both ran.
  let failed = false;
  let failure;
  const start = (effect, begin) => {
    if (!effect) {
      return;
    }

    const work = startWork(effect.timeoutMs);
    works.push(work);

    try {
      begin(effect, work);
    } catch (error) {
      work.finish();
      if (!failed) {
        failed = true;
        failure = error;
      }
    }
  };

  start(effects.scroll, scrollWhenReady);
  start(effects.focus, focusWhenReady);

  if (failed) {
    throw failure;
  }
}
