// Real-browser checks of ?mode=compat: a typical site's App.razor shape (CascadingAuthenticationState, the
// compatibility Router with Found/NotFound, AuthorizeRouteView with NotAuthorized, OnNavigate adding an assembly) over
// the ModRouter engine. Run the sample (or serve a publish with tools/serve.mjs), then:
//   node tools/verify-compat.mjs http://localhost:5199/
// Exits non-zero if any check fails. Needs Chrome (CHROME_PATH overrides the default location).
import { launch, sleep, waitFor } from './cdp.mjs';

const url = process.argv[2] ?? 'http://localhost:5199/';
const basePath = new URL(url).pathname.replace(/\/$/, '');
const page = await launch();
const failures = [];
let checks = 0;

const text = selector => page.evaluate(`return document.querySelector(${JSON.stringify(selector)})?.innerText ?? null;`);
const exists = selector => page.evaluate(`return document.querySelector(${JSON.stringify(selector)}) !== null;`);
const click = id => page.evaluate(`document.getElementById(${JSON.stringify(id)}).click(); return true;`);
// An in-app navigation to any href: Blazor intercepts the click of an anchor in the document, so no full page load.
const go = href => page.evaluate(`const a = document.createElement('a'); a.href = ${JSON.stringify(href)}; document.body.appendChild(a); a.click(); a.remove(); return true;`);
const painted = () => page.evaluate(`return window.__domLog.map(({ t, ...rest }) => rest);`);
const paintedTimes = () => page.evaluate(`return window.__domLog.map(({ t, navigating, page }) => ({ t, navigating, page }));`);
const docTitle = () => page.evaluate(`return document.title;`);
const location = () => page.evaluate(`return location.pathname + location.search;`);
const state = async () => ({
  path: await text('#state-path'), trigger: await text('#state-trigger'), failure: await text('#state-failure'), leaf: await text('#state-leaf'),
  error: await text('#state-error'), onNavigate: await text('#state-on-navigate'), onNavigatePath: await text('#state-on-navigate-path'), canceled: await text('#state-on-navigate-canceled'),
});
const withMode = (path, extra = '') => url.replace(/\/$/, '') + path + (path.includes('?') ? '&' : '?') + 'mode=compat' + extra;
const consoleProblems = () => page.messages.filter(m => /without a prepared navigation|^fail:|^crit:|Unhandled exception/.test(m) || m.startsWith('EXCEPTION'));

function check(name, actual, expected) {
  checks++;
  const ok = JSON.stringify(actual) === JSON.stringify(expected);
  console.log(`${ok ? 'PASS' : 'FAIL'}  ${name}${ok ? '' : `\n      expected ${JSON.stringify(expected)}\n      actual   ${JSON.stringify(actual)}`}`);
  if (!ok) failures.push(name);
}

async function open(path, ready, extra = '') {
  page.messages.length = 0;
  await page.navigate(withMode(path, extra));
  await waitFor(page, ready);
  await sleep(400);
}

const PAGE = { loading: false, navigating: false, layout: true, page: true, notFound: false, error: false };
const NAVIGATING = { loading: false, navigating: true, layout: false, page: false, notFound: false, error: false };

try {
  console.log('==== compat mode ====');
  await open('/', `document.getElementById('home') !== null`);
  check('compat: / renders the discovered home page inside MainLayout, one painted state', [await exists('#app header #session'), await exists('#home'), await painted()], [true, true, [PAGE]]);
  check('compat: OnNavigateAsync ran once before the first navigation, with the built-in path form (base-relative, query included)', [(await state()).onNavigate, (await state()).onNavigatePath], ['1', '?mode=compat']);
  check('compat: the title comes from the discovered page', await docTitle(), 'Home - Router sample');

  await open('/customers/5/orders/4ec9a2a2-1b9f-47b9-8d1a-6e8b2f2b9d77', `document.getElementById('order') !== null || document.getElementById('body') !== null`);
  check('compat: a deep link renders the nested discovered chain through Found > AuthorizeRouteView > outlet', [await exists('#customers #details #order'), (await state()).leaf, await painted()], [true, 'customer-order-details', [PAGE]]);
  check('compat: the deep link path given to OnNavigateAsync', (await state()).onNavigatePath, 'customers/5/orders/4ec9a2a2-1b9f-47b9-8d1a-6e8b2f2b9d77?mode=compat');

  await open('/extra/3', `document.getElementById('extra-child') !== null || document.getElementById('body') !== null`);
  check('compat: a page of the assembly OnNavigateAsync added is routable on the very first navigation', [await text('#extra-child'), (await state()).leaf], ['Extra child 3', 'extra-child']);

  await open('/', `document.getElementById('home') !== null`);
  const layoutBefore = await text('#layout-instance');
  await click('link-customers'); await sleep(400);
  await click('link-c42'); await sleep(400);
  await click('link-c43'); await sleep(400);
  check('compat: navigating keeps the layout instance and the parent page instance; the child updates in place', [await text('#layout-instance') === layoutBefore, await text('#customers h2 small'), await text('#details h3 small')], [true, '(instance #1, parameters set 1x)', '(instance #1, parameters set 2x)']);
  check('compat: OnNavigateAsync ran for every navigation with the new path', [(await state()).onNavigate, (await state()).onNavigatePath], ['4', 'customers/43']);
  await page.evaluate(`history.back(); return true;`); await sleep(500);
  check('compat: back', [(await state()).path, (await state()).trigger, await text('#details h3')], ['/customers/42', 'HistoryRestore', 'Customer 42 (instance #1, parameters set 3x)']);
  await page.evaluate(`history.forward(); return true;`); await sleep(500);
  check('compat: forward', [(await state()).path, (await state()).trigger], ['/customers/43', 'HistoryRestore']);
  await click('link-nope'); await sleep(400);
  check('compat: not-found renders the app NotFound fragment inside its own LayoutView (new layout instance, like the built-in; the trailing ; of @RedirectToDashboard(); is literal text, as on a typical site)', [await exists('#app header'), await text('#body'), (await state()).failure, await text('#layout-instance') !== layoutBefore], [true, 'Not found (compat).;', 'NotFound', true]);
  await click('link-home'); await sleep(400);
  check('compat: back to a page from not-found', [await exists('#home'), await exists('#not-found')], [true, false]);
  check('compat: clean console so far', consoleProblems(), []);

  // [Authorize] from the folder's _Imports.razor plus Roles on the page, enforced by the router's guard; the app's
  // NotAuthorized fragment renders RedirectToLogin for an anonymous user; a sign-in or sign-out re-runs the guard.
  await open('/reports', `document.getElementById('login') !== null || document.getElementById('reports') !== null`);
  // The return URL is rooted and carries the base path, like the sample guard's ("/myapp/reports?mode=compat" under "/myapp/").
  check('compat: anonymous on an [Authorize(Roles)] page: NotAuthorized -> app fragment -> RedirectToLogin -> login page with returnUrl', [await exists('#login'), await text('#login-return'), await location()], [true, basePath + '/reports?mode=compat', basePath + '/login?returnUrl=' + encodeURIComponent(basePath + '/reports?mode=compat')]);
  await click('btn-login-user'); await sleep(600);
  check('compat: a signed-in user without the role gets the app NotAuthorized content (authenticated branch) inside the layout', [await text('#body'), (await state()).error, await exists('#app header')], ['Not authorized (compat): ReportsPage at /reports.', 'NotAuthorized', true]);
  await click('btn-admin'); await sleep(600);
  check('compat: signing in as admin re-runs the authorization guard and reloads: the page appears', [await exists('#reports'), (await state()).error, (await state()).trigger], [true, '', 'Reload']);
  await click('btn-signout'); await sleep(800);
  check('compat: signing out on the page: not-authorized again, anonymous -> RedirectToLogin -> login page', [await exists('#login'), (await state()).path], [true, '/login']);
  check('compat: no loop (one OnNavigate per navigation)', Number((await state()).onNavigate) <= 6, true);

  await click('btn-login-user'); await sleep(600);
  check('compat: signing in on the login page returns to the protected page: the user has no role, so the authenticated NotAuthorized branch', [(await state()).path, await text('#body')], ['/reports', 'Not authorized (compat): ReportsPage at /reports.']);
  await go('profile'); await sleep(500);
  check('compat: a page with only the folder [Authorize] (any signed-in user) renders for the user', [await exists('#profile'), (await state()).error], [true, '']);
  await click('btn-signout'); await sleep(800);
  check('compat: signing out on that page sends the user to the login page (the in-app link carried no query)', [await exists('#login'), await text('#login-return')], [true, basePath + '/profile']);
  await go('notice'); await sleep(500);
  check('compat: an [AllowAnonymous] page in the authorized folder renders for an anonymous user', [await exists('#notice'), (await state()).error], [true, '']);
  await click('btn-admin'); await sleep(600);
  check('compat: a sign-in on a page without an authorization outcome change reloads nothing', [await exists('#notice'), (await state()).trigger], [true, 'Link']);
  check('compat: clean console after the auth checks', consoleProblems(), []);

  // A slow OnNavigateAsync (1.5 s): the first pass shows Navigating bare; later ones keep the old page and show the indicator.
  await open('/customers', `document.getElementById('list') !== null`, '&slownav=1');
  const log = await paintedTimes();
  check('compat slownav: the first pass shows Navigating (no layout) until OnNavigateAsync returns, then the page', await painted(), [NAVIGATING, PAGE]);
  check('compat slownav: Navigating stayed for about the 1.5 s', log.length === 2 && log[1].t - log[0].t >= 1300, true);
  await click('link-c42'); await sleep(700);
  check('compat slownav: a later slow OnNavigateAsync keeps the old page and shows the navigation indicator', [await exists('#list'), await exists('#details'), await exists('#busy')], [true, false, true]);
  await sleep(1200);
  check('compat slownav: then the page', [await exists('#details'), await exists('#busy')], [true, false]);
  await click('link-c43'); await sleep(300);
  await click('link-home'); await sleep(2200);
  check('compat slownav: a superseded navigation had its OnNavigateAsync token canceled', [(await state()).canceled, await exists('#home'), (await state()).path], ['1', true, '/']);
  check('compat slownav: clean console', consoleProblems(), []);
} catch (error) {
  failures.push('script error: ' + error.message);
  console.log('ERROR ' + error.message + '\n' + page.messages.slice(-10).join('\n'));
} finally {
  await page.close();
}

console.log(`\n${checks - failures.length}/${checks} checks passed${failures.length ? '; failed: ' + failures.join('; ') : ''}`);
process.exit(failures.length ? 1 : 0);
