// Real-browser acceptance check for ModRouter. Run the sample first, then:
//   node tools/verify.mjs http://localhost:5199/
// For the base-path variant serve with `--pathbase=/myapp` and a <base href="/myapp/"> and pass .../myapp/.
// Exits non-zero if any check fails. Needs Chrome (set CHROME_PATH if it is not the default location).
// 104 assertions: 26 for navigation and rendering, 33 for guards, loaders, redirects and error states, 37 for the title,
// announcement, focus, scroll, route links and breadcrumb (the last 7 with the content in a scrolling container; the focus
// ones include a page without a heading next to a layout that carries a permanent role="alert") and 8 for the module's
// handling of a bad focus selector, scroll container or fragment.
import { launch, sleep, waitFor } from './cdp.mjs';

const url = process.argv[2] ?? 'http://localhost:5199/';
// The guard's return URL is rooted, so under a base path ("/myapp/") it carries the base path.
const basePath = new URL(url).pathname.replace(/\/$/, '');
const page = await launch();
const failures = [];
let checks = 0;

const text = selector => page.evaluate(`return document.querySelector(${JSON.stringify(selector)})?.innerText ?? null;`);
const exists = selector => page.evaluate(`return document.querySelector(${JSON.stringify(selector)}) !== null;`);
const click = id => page.evaluate(`document.getElementById(${JSON.stringify(id)}).click(); return true;`);
const state = async () => ({
  path: await text('#state-path'),
  query: await text('#state-query'),
  trigger: await text('#state-trigger'),
  failure: await text('#state-failure'),
  leaf: await text('#state-leaf'),
});

/** Parses "instance #N, parameters set Mx" out of an element's text. */
const counters = async selector => {
  const match = /instance #(\d+), parameters set (\d+)x/.exec(await text(selector) ?? '');
  return match ? { instance: Number(match[1]), parametersSet: Number(match[2]) } : null;
};

function check(name, actual, expected) {
  checks++;
  const ok = JSON.stringify(actual) === JSON.stringify(expected);
  console.log(`${ok ? 'PASS' : 'FAIL'}  ${name}${ok ? '' : `\n      expected ${JSON.stringify(expected)}\n      actual   ${JSON.stringify(actual)}`}`);
  if (!ok) failures.push(name);
}

async function step(name, action, settleMs = 450) {
  await action();
  await sleep(settleMs);
  console.log(`-- ${name}`);
}

try {
  await page.navigate(url);
  await waitFor(page, `document.getElementById('home') !== null`);
  await page.evaluate(`window.__bootMarker = 'booted-once'; return true;`);

  await step('initial load', async () => {});
  check('initial load commits the home route', await state(), { path: '/', query: '', trigger: 'InitialLoad', failure: 'None', leaf: 'home' });
  const layout = await text('#layout-instance');

  await step('click /customers', () => click('link-customers'));
  check('link click: index route renders in the parent outlet', [await exists('#customers #list'), (await state()).trigger], [true, 'Link']);
  const customers = await counters('#customers h2 small');

  await step('click /customers/42', () => click('link-c42'));
  check('parent kept, index child replaced by details', [await exists('#customers #details'), await exists('#list'), (await state()).leaf], [true, false, 'customer-details']);
  check('the parent was not recreated and received no new parameters', await counters('#customers h2 small'), customers);
  const details42 = await counters('#details h3 small');

  await step('click /customers/43', () => click('link-c43'));
  const details43 = await counters('#details h3 small');
  check('same child instance, parameters updated in place', [details43.instance, details43.parametersSet], [details42.instance, details42.parametersSet + 1]);
  check('the title factory sees the inherited parameters', await text('#state-title'), 'Customer 43');

  await step('click order (grandchild)', () => click('link-order'));
  check('grandchild renders with its own and its ancestor parameters', /^Order 4ec9a2a2-1b9f-47b9-8d1a-6e8b2f2b9d77 of customer 42 /.test(await text('#order')), true);

  await step('programmatic NavigateTo("customers/43")', () => click('btn-nav'));
  check('programmatic navigation is classified as such', await state(), { path: '/customers/43', query: '', trigger: 'Programmatic', failure: 'None', leaf: 'customer-details' });

  await step('NavigateToRoute by name', () => click('btn-route-42'));
  check('named-route navigation goes through the same pipeline', (await state()).path, '/customers/42');

  // A rooted URL ("/customers/43", or "/myapp/customers/43" under a base path): on browser-wasm a standalone absolute
  // parse would read it as file:///customers/43 and skip the pre-commit pipeline (the console check below would then
  // see the "without a prepared navigation" warning).
  await step('programmatic NavigateTo with a rooted URL from GetRouteUrl', () => click('btn-nav-rooted'));
  check('a root-relative NavigateTo is prepared and committed like any other navigation', await state(), { path: '/customers/43', query: '', trigger: 'Programmatic', failure: 'None', leaf: 'customer-details' });

  await step('NavigateToRoute with ambient values (id from the current route)', async () => {
    await click('link-c43');
    await sleep(400);
    await click('btn-route-order');
  });
  check('ambient customer id is reused', (await state()).path, '/customers/43/orders/4ec9a2a2-1b9f-47b9-8d1a-6e8b2f2b9d77');

  await step('history.back()', () => page.evaluate(`history.back(); return true;`));
  check('back is recognized as a history traversal', [(await state()).path, (await state()).trigger], ['/customers/43', 'HistoryRestore']);
  await step('history.forward()', () => page.evaluate(`history.forward(); return true;`));
  check('forward is recognized as a history traversal', [(await state()).path, (await state()).trigger], ['/customers/43/orders/4ec9a2a2-1b9f-47b9-8d1a-6e8b2f2b9d77', 'HistoryRestore']);

  await step('query link a', () => click('link-query-a'));
  const queryA = await counters('#query');
  check('query parameters are supplied by Blazor under the router', (await text('#query')).startsWith('tab=a q= nums= '), true);
  await step('query-only change', () => click('link-query-b'));
  const queryB = await counters('#query');
  check('query values (named parameter, repeated key) arrive', (await text('#query')).startsWith('tab=b q=hello nums=1,2 '), true);
  check('a query-only change updates the same instance in place', [queryB.instance, queryB.parametersSet], [queryA.instance, queryA.parametersSet + 1]);

  await step('customer 42 then unknown (leaving a page that contains an outlet)', async () => {
    await click('link-c42');
    await sleep(400);
    await click('link-nope');
  });
  check('found to not-found from a page with an outlet renders not-found', [await exists('#body #not-found'), (await state()).failure], [true, 'NotFound']);
  await step('back to customer 42', () => click('link-c42'));
  check('found again after not-found', await exists('#customers #details'), true);

  await step('cross-section: customers to vendors (both sections have child routes)', () => click('link-v5'));
  check('a different depth-0 component with its own outlet replaces the old one', [await exists('#vendors #vendor'), await exists('#customers')], [true, false]);
  await step('vendors back to customers', () => click('link-customers'));
  check('customers renders again after the cross-section round trip', await exists('#customers #list'), true);

  await step('unknown URL', () => click('link-nope'));
  check('not-found renders inside the layout and commits a failure state', [await exists('#body #not-found'), (await state()).failure], [true, 'NotFound']);
  await step('back to a known route', () => click('link-home'));
  check('home renders again', await exists('#home'), true);
  check('the layout instance survived found, not-found and found again', await text('#layout-instance'), layout);

  await step('two quick clicks', () => page.evaluate(`document.getElementById('link-c42').click(); document.getElementById('link-c43').click(); return true;`), 700);
  check('rapid navigation settles on the last one', (await state()).path, '/customers/43');

  // ---- slice 3a: guards, loaders, redirects, error states ------------------------------------------------------------

  const historyLength = () => page.evaluate(`return history.length;`);

  await step('go home, signed out', async () => {
    await click('link-home');
    await sleep(300);
    await click('btn-signout');
  });
  const beforeGuard = await historyLength();
  await step('click /account while signed out', () => click('link-account'));
  check('a guard redirected the signed-out user to the login page', await state(), { path: '/login', query: `?returnUrl=${encodeURIComponent(basePath + '/account')}`, trigger: 'Redirect', failure: 'None', leaf: 'login' });
  check('the login page knows where the user was going', await text('#login-return'), basePath + '/account');
  check('the redirect added exactly one history entry (the redirecting URL never enters history)', (await historyLength()) - beforeGuard, 1);
  check('the redirect is recorded on the state', await text('#state-redirected'), '1');
  await step('history.back() from the login page', () => page.evaluate(`history.back(); return true;`));
  check('back returns to the page the user came from and does not loop', [(await state()).path, (await state()).trigger], ['/', 'HistoryRestore']);

  await step('click /account again, then sign in as a user', async () => {
    await click('link-account');
    await sleep(400);
    await click('btn-login-user');
  });
  check('signing in continues to the original destination', [(await state()).path, await exists('#account #account-home')], ['/account', true]);
  const accountBefore = await counters('#account h2 small');

  await step('click the admin link as a plain user', () => click('link-admin'));
  check('the navigation committed with a not-authorized error at the child level', [(await state()).path, await text('#state-error'), (await state()).failure], ['/account/admin', 'NotAuthorized', 'None']);
  check('the error belongs to chain level 1', await text('#state-failed-level'), '1');
  check('the not-authorized content renders inside the parent', (await text('#account #not-authorized'))?.startsWith('Administrators only.'), true);
  check('the parent stayed mounted and did not re-receive parameters', await counters('#account h2 small'), accountBefore);
  check('the admin page itself did not render', await exists('#admin'), false);

  await step('sign in as admin, then check again', async () => {
    await click('btn-admin');
    await sleep(300);
    await click('btn-retry-na');
  });
  check('retry ran the guard again and the admin page rendered in place', [await exists('#account #admin'), await text('#state-error'), (await state()).trigger], [true, '', 'Reload']);
  check('the parent was still not recreated', (await counters('#account h2 small')).instance, accountBefore.instance);
  await click('btn-signout');

  await step('go home', async () => {
    await click('link-home');
    await sleep(300);
  }, 0);
  const slowStart = await historyLength();
  await step('click the slow page and look while its critical data loads', async () => {
    await click('link-slow');
    await sleep(400);
  }, 0);
  check('the old page and URL are held while the loader runs', [(await state()).path, await exists('#home')], ['/', true]);
  check('the navigation indicator appeared after its delay', await exists('#busy'), true);
  await waitFor(page, `document.getElementById('slow-name') !== null`, 6000);
  check('the page rendered as soon as its critical data arrived', [(await state()).path, await text('#slow-name')], ['/slow/7', 'Customer 7']);
  check('the indicator is gone once the navigation committed', await exists('#busy'), false);
  check('the deferred value and the stream start empty and fill in later', [await exists('#orders-fallback'), (await exists('#feed-fallback')) || (await exists('.feed-item'))], [true, true]);
  await waitFor(page, `document.getElementById('orders') !== null`, 6000);
  check('the deferred value arrived after the page rendered', await text('#orders'), 'order 7-1, order 7-2');
  await waitFor(page, `document.querySelectorAll('.feed-item').length === 5`, 6000);
  check('the stream filled in item by item', await page.evaluate(`return [...document.querySelectorAll('.feed-item')].map(e => e.innerText.trim()).join(',');`), '71,72,73,74,75');
  check('the stream finished (no in-progress marker)', await exists('#feed-more'), false);
  check('a slow navigation added exactly one history entry', (await historyLength()) - slowStart, 1);

  await step('rapid navigation between two slow pages', async () => {
    await click('link-home');
    await sleep(300);
    await click('link-slow');
    await sleep(200);
    await click('link-slow8');
  }, 0);
  await waitFor(page, `document.getElementById('slow-name')?.innerText === 'Customer 8'`, 6000);
  await sleep(1800);
  check('the last navigation wins and the superseded page never renders', [(await state()).path, await text('#slow-name')], ['/slow/8', 'Customer 8']);

  await step('open the flaky page', () => click('link-flaky'), 900);
  check('a loader failure committed with an error view at the child level', [(await state()).path, await text('#state-error'), await text('#state-failed-level')], ['/flaky/1', 'LoadFailed', '1']);
  check('the error view renders inside the parent', (await text('#flaky #route-error'))?.startsWith('LoadFailed: Loading failed: The service is warming up'), true);
  const flakyParent = await counters('#flaky h2 small');
  const flakyHistory = await historyLength();
  await step('retry the failed loader', () => click('btn-retry'), 900);
  check('retry loaded the data and replaced the error in place', [await exists('#route-error'), (await text('#flaky-details'))?.startsWith('Flaky item 1 loaded on attempt 2')], [false, true]);
  check('the parent never remounted across the error and the retry', (await counters('#flaky h2 small')).instance, flakyParent.instance);
  check('a retry does not add a history entry', await historyLength(), flakyHistory);

  await step('click the old customer URL (redirect rule)', () => click('link-old'));
  check('a redirect rule forwarded the parameter to the customer route', [(await state()).path, (await state()).trigger, (await state()).leaf], ['/customers/42', 'Redirect', 'customer-details']);
  await step('click the supplier alias', () => click('link-supplier'));
  check('an alias with canonical redirect landed on the canonical URL', [(await state()).path, (await state()).trigger], ['/vendors/5', 'Redirect']);

  await step('click the redirect loop', () => click('link-loop'));
  check('a redirect loop committed a router-level error', [(await state()).failure, await text('#state-error')], ['RedirectLoop', 'RedirectLoop']);
  check('the router error renders inside the layout', (await text('#body #route-error'))?.startsWith('RedirectLoop: The navigation was redirected in a loop'), true);

  await step('go home, then click the blocked page', async () => {
    await click('link-home');
    await sleep(400);
    await click('link-blocked');
  });
  check('a canceling guard keeps the page, the state and the URL', [(await state()).path, await exists('#home'), await page.evaluate(`return location.pathname;`)], ['/', true, new URL(url).pathname]);

  // ---- slice 3b: title, announcement, focus, scroll, route links and the breadcrumb -----------------------------------

  const docTitle = () => page.evaluate(`return document.title;`);
  const active = () => page.evaluate(`const a = document.activeElement; return a ? a.tagName.toLowerCase() + (a.id ? '#' + a.id : '') : null;`);
  const announcer = async () => (await page.evaluate(`return document.querySelector('[data-mod-router-announcer]')?.textContent ?? null;`))?.replace(/\u00a0/g, '');
  const clickT = testId => page.evaluate(`document.querySelector('[data-testid="${testId}"]').click(); return true;`);
  const attr = (testId, name) => page.evaluate(`return document.querySelector('[data-testid="${testId}"]')?.getAttribute(${JSON.stringify(name)});`);
  const scrollY = () => page.evaluate(`return Math.round(window.scrollY);`);
  const crumbs = () => page.evaluate(`return [...document.querySelectorAll('[data-testid="breadcrumb"] li > a, [data-testid="breadcrumb"] li > span:not([aria-hidden])')].map(e => [e.textContent.trim(), e.getAttribute('href')]);`);
  const suffix = ' - Router sample';

  await step('go home', () => click('link-home'));
  check('the document title follows the route title through the format', await docTitle(), 'Home' + suffix);
  check('the new page is announced in the live region', await announcer(), 'Home' + suffix);
  check('at the root the titled home route is the only crumb (no second Home for the same URL)', await crumbs(), [['Home', null]]);

  await step('click Customers', () => click('link-customers'));
  check('an untitled index route takes its parent\'s title', await docTitle(), 'Customers' + suffix);
  check('a route link to the parent of the index route is the current page', await attr('rl-customers', 'aria-current'), 'page');
  check('a link navigation moves focus to the page heading', await page.evaluate(`return document.activeElement?.tagName === 'H2' && document.activeElement.closest('#customers') !== null;`), true);

  await step('click Customer 42', () => click('link-c42'));
  check('a title factory sees the route parameters', await docTitle(), 'Customer 42' + suffix);
  check('the breadcrumb follows the matched chain with real links and no link on the current page', await crumbs(), [['Home', basePath + '/'], ['Customers', basePath + '/customers'], ['Customer 42', null]]);
  check('a route link to the current page says aria-current="page"', await attr('rl-c42', 'aria-current'), 'page');
  check('a route link to an ancestor says aria-current="true"', await attr('rl-customers', 'aria-current'), 'true');
  check('an exact Href link to an ancestor is not current', await attr('rl-href-customers', 'aria-current'), null);
  check('route links generate base-prefixed hrefs with parameters and fragments', [await attr('rl-c42', 'href'), await attr('rl-anchors-target', 'href')], [basePath + '/customers/42', basePath + '/anchors#target']);

  await step('NavigateTo customers/43', () => click('btn-nav'));
  check('a route link stops being current when the page changes', await attr('rl-c42', 'aria-current'), null);
  const announced = await announcer();
  const focused = await active();
  check('the programmatic navigation was announced', announced, 'Customer 43' + suffix);
  await step('history.back()', () => page.evaluate(`history.back(); return true;`));
  check('back sets the title of the page it returns to', await docTitle(), 'Customer 42' + suffix);
  check('back neither announces nor moves focus', [await announcer(), await active()], [announced, focused]);

  await step('unknown URL', () => click('link-nope'));
  check('a URL that matches nothing gets the not-found title', await docTitle(), 'Not found');

  await step('type in the search box, then click Customers', async () => {
    await page.evaluate(`document.getElementById('search').focus(); return true;`);
    await click('link-customers');
  });
  check('focus is not taken from somebody typing, but the title still follows', [await active(), await docTitle()], ['input#search', 'Customers' + suffix]);
  await page.evaluate(`document.getElementById('search').blur(); return true;`);

  await step('open the late-heading page', async () => {
    await clickT('rl-late');
    await sleep(120);
  }, 0);
  check('focus waits for a heading that is not rendered yet', await active(), 'body');
  await waitFor(page, `document.activeElement?.id === 'late-heading'`, 4000);
  check('and lands on it once it renders', await active(), 'h1#late-heading');

  // The layout carries a permanent role="alert" (#layout-alert) that the router must never focus: the fallback is the router's own error content.
  await step('open the page without a heading', async () => {
    await clickT('rl-no-heading');
    await sleep(300);
  }, 0);
  const alertEarly = await active();
  await sleep(1800);
  check('a page without a heading does not give focus to the alert of the layout, now or when the focus wait is over', [await exists('#layout-alert'), await exists('#no-heading'), alertEarly, await active()], [true, true, 'body', 'body']);

  await step('click the redirect loop', () => click('link-loop'), 800);
  check('router-level error content takes focus when no heading matches', await active(), 'p#route-error');
  check('the layout alert is still there, and the router error sits in the error host that does not affect the layout', await page.evaluate(`
    const host = document.querySelector('[data-mod-route-error-host]');
    return [document.getElementById('layout-alert') !== null, host?.querySelector('p#route-error') !== null, host ? getComputedStyle(host).display : null, document.querySelectorAll('[data-mod-route-error-host]').length];`), [true, true, 'contents', 1]);

  await step('open the long page and scroll it', async () => {
    await clickT('rl-long1');
    await sleep(500);
    await page.evaluate(`window.scrollTo(0, 900); return true;`);
    await sleep(300);
  }, 0);
  check('the long page is scrolled', await scrollY(), 900);
  await step('long page 2', () => clickT('rl-long2'), 600);
  check('a new navigation starts at the top', await scrollY(), 0);
  await page.evaluate(`window.scrollTo(0, 400); return true;`);
  await sleep(300);
  await step('history.back() to long 1', () => page.evaluate(`history.back(); return true;`), 900);
  check('back restores the position the page had', await scrollY(), 900);
  await step('history.forward() to long 2', () => page.evaluate(`history.forward(); return true;`), 900);
  check('forward restores the position of that entry, not the previous one', await scrollY(), 400);

  // A page the user never scrolled has no saved position: back to it means the top, not wherever the later page was.
  // Home first, so the window is already at the top when long 1 opens and no scroll event (not even the router's own
  // scroll to the top) records a position for long 1's history entry.
  await step('home, long 1 (a fresh entry, never scrolled), long 2 scrolled, then back', async () => {
    await clickT('rl-home');
    await sleep(500);
    await clickT('rl-long1');
    await sleep(500);
    await clickT('rl-long2');
    await sleep(500);
    await page.evaluate(`window.scrollTo(0, 500); return true;`);
    await sleep(300);
    await page.evaluate(`history.back(); return true;`);
  }, 900);
  check('back to a page that was never scrolled lands at the top', await scrollY(), 0);

  await step('link to a fragment that renders late', () => clickT('rl-anchors-target'), 1400);
  check('a fragment scrolls to its target once it exists', await page.evaluate(`const top = document.getElementById('target')?.getBoundingClientRect().top; return top !== undefined && top > -5 && top < 80;`), true);
  check('the fragment page sets its title', await docTitle(), 'Anchors' + suffix);

  check('no navigation caused a full page reload', await page.evaluate(`return window.__bootMarker;`), 'booted-once');

  // The page already holds the module (same URL, same instance), so its pending-work bookkeeping can be probed directly:
  // an effect that is satisfied at once must release its abandon listeners right away, not when its deadline passes.
  check('an immediately satisfied effect releases its window listeners at once', await page.evaluate(`
    const module = await import(new URL('_content/ModRouter/mod-router.js', document.baseURI).href);
    // An empty run first: it finishes whatever the last real navigation left pending, so only the probe's own work is counted.
    module.afterNavigation({ title: null, focus: null, scroll: null });
    const add = window.addEventListener, remove = window.removeEventListener;
    let added = 0, removed = 0;
    window.addEventListener = function (name, fn, options) { if (name === 'wheel') added++; return add.call(this, name, fn, options); };
    window.removeEventListener = function (name, fn, options) { if (name === 'wheel') removed++; return remove.call(this, name, fn, options); };
    try {
      module.afterNavigation({ title: null, focus: { selector: '#state-path', timeoutMs: 60000 }, scroll: null });
    } finally {
      window.addEventListener = add;
      window.removeEventListener = remove;
    }
    return { added, removed };`), { added: 1, removed: 1 });

  // trackScroll runs after every navigation: a container that does not exist yet is not an error, one that appears later is
  // tracked from then on (its position is saved and restored), and asking again never adds a second listener.
  check('scroll tracking tolerates a missing container, picks it up once it exists and tracks it only once', await page.evaluate(`
    const module = await import(new URL('_content/ModRouter/mod-router.js', document.baseURI).href);
    module.trackScroll('#late-container');
    const late = document.createElement('div');
    late.id = 'late-container';
    late.style.cssText = 'height:100px;overflow:auto';
    late.innerHTML = '<div style="height:2000px"></div>';
    document.body.appendChild(late);
    let listeners = 0;
    const add = late.addEventListener;
    late.addEventListener = function (name, fn, options) { if (name === 'scroll') listeners++; return add.call(this, name, fn, options); };
    module.trackScroll('#late-container');
    module.trackScroll('#late-container');
    late.scrollTop = 300;
    await new Promise(resolve => setTimeout(resolve, 100));
    late.scrollTop = 0;
    module.afterNavigation({ title: null, focus: null, scroll: { mode: 'restore', container: '#late-container', timeoutMs: 1000 } });
    const restored = Math.round(late.scrollTop);
    late.remove();
    return { listeners, restored };`), { listeners: 1, restored: 300 });

  // ---- slice 4e: a bad value reaches the module (an app's Focus.Selector, a fragment typed in the address bar) -------------
  // The module is called directly: no page can be made to send these values. The error host stands in for the router's own
  // error content, so the focus fallback has an alert to find in the old module and the new one alike.
  const badInput = await page.evaluate(`
    const module = await import(new URL('_content/ModRouter/mod-router.js', document.baseURI).href);
    const sleep = ms => new Promise(resolve => setTimeout(resolve, ms));
    const events = ['wheel', 'touchstart', 'keydown', 'mousedown'];
    const counts = { added: 0, removed: 0 };
    const add = window.addEventListener, remove = window.removeEventListener;
    window.addEventListener = function (name, fn, options) { if (events.includes(name)) counts.added++; return add.call(this, name, fn, options); };
    window.removeEventListener = function (name, fn, options) { if (events.includes(name)) counts.removed++; return remove.call(this, name, fn, options); };
    const warn = console.warn;
    const warnings = [];
    console.warn = (...args) => { warnings.push(args.join(' ')); };
    const spacer = document.createElement('div');
    spacer.style.cssText = 'height:4000px';
    const target = document.createElement('div');
    target.id = '%E0%A4';
    target.style.cssText = 'height:20px';
    const host = document.createElement('div');
    host.setAttribute('data-mod-route-error-host', '');
    host.innerHTML = '<p id="probe-alert" role="alert">probe</p>';
    const run = effects => { try { module.afterNavigation(effects); return null; } catch (error) { return error.name; } };
    const result = {};
    try {
      // An empty run first: it finishes whatever the last real navigation left pending, so only the probe's own work is counted.
      module.afterNavigation({ title: null, focus: null, scroll: null });
      document.body.append(spacer, target, host);
      window.scrollTo(0, 0);
      document.activeElement?.blur?.();
      counts.added = 0;
      counts.removed = 0;

      result.threw = run({ title: null, focus: { selector: 'h1[', timeoutMs: 500 }, scroll: { mode: 'fragment', fragment: '%E0%A4', timeoutMs: 500 } });
      result.atOnce = { added: counts.added, removed: counts.removed };
      result.scrolled = window.scrollY > 1000;
      result.focused = document.activeElement?.id ?? null;

      run({ title: null, focus: { selector: 'h1[', timeoutMs: 500 }, scroll: null });
      result.warnings = warnings.filter(w => w.includes('h1[')).length;

      document.activeElement?.blur?.();
      result.threwContainer = run({ title: null, focus: { selector: '#state-path', timeoutMs: 500 }, scroll: { mode: 'top', container: 'h1[', timeoutMs: 500 } });
      result.focusedAfterBadContainer = document.activeElement?.id ?? null;

      document.activeElement?.blur?.();
      window.scrollTo(0, 2000);
      run({ title: null, focus: { selector: '#state-query', timeoutMs: 500 }, scroll: { mode: 'top', timeoutMs: 500 } });
      result.next = [document.activeElement?.id ?? null, Math.round(window.scrollY)];

      result.threwEmpty = run({ title: null, focus: null, scroll: { mode: 'fragment', fragment: '', timeoutMs: 200 } });
      await sleep(900);
      result.released = counts.added > 0 && counts.added === counts.removed;
    } finally {
      window.addEventListener = add;
      window.removeEventListener = remove;
      console.warn = warn;
      spacer.remove();
      target.remove();
      host.remove();
      window.scrollTo(0, 0);
    }
    return result;`);
  check('a malformed fragment and an invalid focus selector do not throw out of afterNavigation', badInput.threw, null);
  check('a malformed fragment is matched as written: the element with that literal id is scrolled to', badInput.scrolled, true);
  check('an invalid focus selector is no match: the error alert takes focus instead', badInput.focused, 'probe-alert');
  check('an invalid focus selector is warned about once, however often it is used', badInput.warnings, 1);
  check('effects that finish at once release their abandon listeners at once, a failed one included', badInput.atOnce, { added: 8, removed: 8 });
  check('a scroll container that is not a valid selector fails on its own: the focus effect still runs and the failure is still reported', [badInput.threwContainer, badInput.focusedAfterBadContainer], ['SyntaxError', 'state-path']);
  check('a navigation after the failed ones still moves focus and scrolls to the top', badInput.next, ['state-query', 0]);
  check('an empty fragment never throws, and every listener is released by the deadlines', [badInput.threwEmpty, badInput.released], [null, true]);

  // ---- slice 3b with the content in a scrolling container (?scroller=1) ----------------------------------------------

  await page.navigate(url + (url.includes('?') ? '&' : '?') + 'scroller=1');
  await waitFor(page, `document.getElementById('scroller') !== null && document.querySelector('h1') !== null`);
  await sleep(600);
  const containerTop = () => page.evaluate(`return Math.round(document.getElementById('scroller').scrollTop);`);

  await step('container: open the long page and scroll the container', async () => {
    await clickT('rl-long1');
    await sleep(500);
    await page.evaluate(`document.getElementById('scroller').scrollTop = 900; return true;`);
    await sleep(300);
  }, 0);
  check('the container is scrolled and the window is not', [await containerTop(), await scrollY()], [900, 0]);
  await step('container: long page 2', () => clickT('rl-long2'), 600);
  check('a new navigation scrolls the container to the top (Blazor does not)', await containerTop(), 0);
  await page.evaluate(`document.getElementById('scroller').scrollTop = 400; return true;`);
  await sleep(300);
  await step('container: history.back()', () => page.evaluate(`history.back(); return true;`), 900);
  check('back restores the container position', await containerTop(), 900);
  await step('container: history.forward()', () => page.evaluate(`history.forward(); return true;`), 900);
  check('forward restores that entry\'s container position', [await containerTop(), await scrollY()], [400, 0]);

  await step('container: home, long 1 (a fresh entry, never scrolled), long 2 scrolled, then back', async () => {
    await clickT('rl-home');
    await sleep(500);
    await clickT('rl-long1');
    await sleep(500);
    await clickT('rl-long2');
    await sleep(500);
    await page.evaluate(`document.getElementById('scroller').scrollTop = 500; return true;`);
    await sleep(300);
    await page.evaluate(`history.back(); return true;`);
  }, 900);
  check('back to a page whose container was never scrolled lands the container at the top', await containerTop(), 0);

  // The flaky loader and the redirect loop are logged as errors on purpose; anything else is a problem.
  const expected = /the loader of level|redirected in a loop/;
  check('no router warnings, unexpected failures or exceptions in the browser console', page.messages.filter(m => (/without a prepared navigation|^fail:|^crit:|Unhandled exception/.test(m) && !expected.test(m)) || m.startsWith('EXCEPTION')), []);
} catch (error) {
  failures.push('script error: ' + error.message);
  console.log('ERROR ' + error.message);
} finally {
  await page.close();
}

console.log(`\n${checks - failures.length}/${checks} checks passed${failures.length ? '; failed: ' + failures.join('; ') : ''}`);
process.exit(failures.length ? 1 : 0);
