// Real-browser checks of the slice 4a route sources: the markup facade (?mode=markup, and the compile error of
// ?mode=markup-dup) and page discovery (?mode=pages). The default mode keeps tools/verify.mjs. Run the sample (or serve a
// publish with tools/serve.mjs), then:
//   node tools/verify-modes.mjs http://localhost:5199/ [markup|pages|all]
// EXPECT_EXTRA=missing makes the pages run expect the Extra assembly's pages to be absent (a trimmed publish without the
// TrimmerRootAssembly). Exits non-zero if any check fails. Needs Chrome (CHROME_PATH overrides the default location).
import { launch, sleep, waitFor } from './cdp.mjs';

const url = process.argv[2] ?? 'http://localhost:5199/';
const which = process.argv[3] ?? 'all';
const basePath = new URL(url).pathname.replace(/\/$/, '');
const page = await launch();
const failures = [];
let checks = 0;

const text = selector => page.evaluate(`return document.querySelector(${JSON.stringify(selector)})?.innerText ?? null;`);
const exists = selector => page.evaluate(`return document.querySelector(${JSON.stringify(selector)}) !== null;`);
const attribute = (selector, name) => page.evaluate(`return document.querySelector(${JSON.stringify(selector)})?.getAttribute(${JSON.stringify(name)}) ?? null;`);
const click = id => page.evaluate(`document.getElementById(${JSON.stringify(id)}).click(); return true;`);
// An in-app navigation to any href: Blazor intercepts the click of an anchor in the document, so no full page load.
const go = href => page.evaluate(`const a = document.createElement('a'); a.href = ${JSON.stringify(href)}; document.body.appendChild(a); a.click(); a.remove(); return true;`);
const painted = () => page.evaluate(`return window.__domLog.map(({ t, ...rest }) => rest);`);
const docTitle = () => page.evaluate(`return document.title;`);
const crumbs = () => page.evaluate(`return [...document.querySelectorAll('[data-testid="breadcrumb"] li > a, [data-testid="breadcrumb"] li > span:not([aria-hidden])')].map(e => [e.textContent.trim(), e.getAttribute('href')]);`);
const state = async () => ({ path: await text('#state-path'), trigger: await text('#state-trigger'), failure: await text('#state-failure'), leaf: await text('#state-leaf') });
const withMode = (path, mode) => url.replace(/\/$/, '') + path + (path.includes('?') ? '&' : '?') + 'mode=' + mode;
const consoleProblems = () => page.messages.filter(m => /without a prepared navigation|^fail:|^crit:|Unhandled exception/.test(m) || m.startsWith('EXCEPTION'));

function check(name, actual, expected) {
  checks++;
  const ok = JSON.stringify(actual) === JSON.stringify(expected);
  console.log(`${ok ? 'PASS' : 'FAIL'}  ${name}${ok ? '' : `\n      expected ${JSON.stringify(expected)}\n      actual   ${JSON.stringify(actual)}`}`);
  if (!ok) failures.push(name);
}

async function open(path, mode, ready) {
  page.messages.length = 0;
  await page.navigate(withMode(path, mode));
  await waitFor(page, ready);
  await sleep(400);
}

const PAGE = { loading: false, navigating: false, layout: true, page: true, notFound: false, error: false };
const BARE_LOADING = { loading: true, navigating: false, layout: false, page: false, notFound: false, error: false };

try {
  if (which === 'all' || which === 'markup') {
    console.log('==== markup mode ====');
    await open('/', 'markup', `document.getElementById('home') !== null`);
    check('markup: / renders the startup home route', (await state()).leaf, 'home');
    // Every stage is synchronous at /: the bare first pass and the chain land in one JS task, so only the page is painted.
    check('markup: at / the user sees exactly one painted state, the page in its layout (no Loading, no not-found)', await painted(), [PAGE]);
    check('markup: GetRouteUrl for a markup-only route name after the first render', await text('#route-url'), basePath + '/customers/42');
    check('markup: a route link by a markup route name', await attribute('[data-testid="rl-c42"]', 'href'), basePath + '/customers/42');

    await open('/customers/5', 'markup', `document.getElementById('details') !== null`);
    check('markup: a deep link renders the nested markup route', [(await text('#details')).startsWith('Customer 5'), (await state()).leaf], [true, 'customer-details']);
    check('markup: the deep link is the first painted state (no detour through not-found)', await painted(), [PAGE]);
    check('markup: the markup title factory sets the document title', await docTitle(), 'Customer 5 - Router sample');
    check('markup: the breadcrumb follows the markup chain with links by route name', await crumbs(), [['Home', basePath + '/'], ['Customers', basePath + '/customers'], ['Customer 5', null]]);

    await open('/customers/5/orders/4ec9a2a2-1b9f-47b9-8d1a-6e8b2f2b9d77', 'markup', `document.getElementById('order') !== null`);
    check('markup: a three-level markup chain renders', [await exists('#customers #details #order'), (await state()).leaf], [true, 'customer-order-details']);

    await open('/slow/7', 'markup', `document.getElementById('slow-name') !== null`);
    check('markup: a slow deep link shows bare Loading (no layout, no not-found), then the page in its layout', await painted(), [BARE_LOADING, PAGE]);

    await open('/', 'markup', `document.getElementById('home') !== null`);
    await click('link-c42'); await sleep(400);
    await click('link-c43'); await sleep(400);
    await page.evaluate(`history.back(); return true;`); await sleep(500);
    check('markup: back', [(await state()).path, (await state()).trigger], ['/customers/42', 'HistoryRestore']);
    await page.evaluate(`history.forward(); return true;`); await sleep(500);
    check('markup: forward', [(await state()).path, (await state()).trigger], ['/customers/43', 'HistoryRestore']);
    await click('btn-route-order'); await sleep(400);
    check('markup: NavigateToRoute by a nested markup name with ambient values', (await state()).path, '/customers/43/orders/4ec9a2a2-1b9f-47b9-8d1a-6e8b2f2b9d77');
    await click('link-nope'); await sleep(400);
    check('markup: not-found still works', [await exists('#not-found'), (await state()).failure], [true, 'NotFound']);
    await click('link-account'); await sleep(400);
    check('markup: a startup guard route still redirects', (await state()).path, '/login');
    check('markup: clean console so far', consoleProblems(), []);

    // The static-content rules: a changed parameter is ignored, a new entry throws (the renderer logs it and goes on).
    // The navigations here must stay in-app: a full page load would reboot the app and register the entry afresh.
    await click('link-home'); await sleep(300);
    await click('btn-move-entry'); await sleep(400);
    await page.evaluate(`document.querySelector('[data-testid="rl-home"]').click(); return true;`); await sleep(300);
    await go('about'); await sleep(500);
    check('markup: a changed Path after the freeze is ignored: the old path still matches (in-app, no reload)', [await exists('#query'), (await state()).path, (await state()).trigger], [true, '/about', 'Link']);
    await go('about-moved'); await sleep(500);
    check('markup: and the new path does not match', [await exists('#not-found'), (await state()).failure], [true, 'NotFound']);
    page.messages.length = 0;
    await open('/', 'markup', `document.getElementById('home') !== null`);
    await click('btn-late-entry'); await sleep(800);
    check('markup: an entry rendered after the freeze throws (an unhandled render exception in the console)', page.messages.some(m => m.includes('The route table is already compiled')), true);
    await click('link-customers'); await sleep(500);
    check('markup: the app keeps navigating after that exception', await exists('#customers'), true);

    await open('/', 'markup-dup', `document.getElementById('route-error') !== null || document.getElementById('home') !== null`);
    check('markup-dup: the duplicate name is rendered through the Error fragment as ConfigurationFailed', [(await text('#route-error') ?? '').startsWith('ConfigurationFailed:'), (await text('#route-error') ?? '').includes('RT0010'), (await text('#route-error') ?? '').includes('markup: <ModRouteEntry Path="/about"')], [true, true, true]);
    check('markup-dup: no endless Loading, no page, no layout', [await exists('#loading'), await exists('#home'), await exists('#app header')], [false, false, false]);
    check('markup-dup: the error is logged, not thrown as an unhandled render exception', [page.messages.some(m => m.includes('RT0010')), consoleProblems().filter(m => !m.includes('RT0010'))], [true, []]);
  }

  if (which === 'all' || which === 'pages') {
    console.log('==== pages mode ====');
    const expectExtra = process.env.EXPECT_EXTRA !== 'missing';
    await open('/', 'pages', `document.getElementById('home') !== null || document.getElementById('not-found') !== null`);
    check('pages: / renders the discovered home page in one painted state', [await exists('#home'), await painted()], [true, [PAGE]]);
    check('pages: titles come from [RouteTitle]', await docTitle(), 'Home - Router sample');
    check('pages: route links by [RouteName]', await attribute('[data-testid="rl-c42"]', 'href'), basePath + '/customers/42');

    await open('/customers/5/orders/4ec9a2a2-1b9f-47b9-8d1a-6e8b2f2b9d77', 'pages', `document.getElementById('order') !== null || document.getElementById('not-found') !== null`);
    check('pages: nested discovered pages ([RouteParent]) render as a chain', [await exists('#customers #details #order'), (await state()).leaf], [true, 'customer-order-details']);
    check('pages: the deep link is the first painted state', await painted(), [PAGE]);
    check('pages: the nested page title', await docTitle(), 'Order - Router sample');

    await open('/customers', 'pages', `document.getElementById('list') !== null || document.getElementById('not-found') !== null`);
    check('pages: the [RouteIndex] page renders in the parent outlet', await exists('#customers #list'), true);

    await open('/vendors/5', 'pages', `document.getElementById('vendor') !== null || document.getElementById('not-found') !== null`);
    check('pages: breadcrumb labels from [RouteBreadcrumb], links by [RouteName]', await crumbs(), [['Home', basePath + '/'], ['Vendors', basePath + '/vendors'], ['Vendor', null]]);
    await open('/suppliers/5', 'pages', `document.getElementById('vendor') !== null || document.getElementById('not-found') !== null`);
    check('pages: a second @page is an alias and [RouteCanonicalAlias] redirects it', [(await state()).path, (await state()).trigger], ['/vendors/5', 'Redirect']);

    await click('btn-signout'); await sleep(300);
    await click('link-account'); await sleep(500);
    check('pages: a guard declared with @attribute [RouteGuard<SignedInGuard>] redirects', (await state()).path, '/login');
    await click('btn-admin'); await sleep(300);
    await page.evaluate(`document.getElementById('link-admin-direct').click(); return true;`); await sleep(500);
    check('pages: a guard declared with [RouteGuard(typeof(...))] allows the admin', await exists('#account #admin'), true);

    await open('/slow/7', 'pages', `document.getElementById('slow-name') !== null`);
    check('pages: a loader declared with [RouteLoader<T>] loads the page', await exists('#slow #slow-name'), true);

    await open('/old/customers/42', 'pages', `document.getElementById('details') !== null || document.getElementById('not-found') !== null`);
    check('pages: a fluent redirect rule targets a discovered route by name', (await state()).path, '/customers/42');

    await open('/extra/3', 'pages', `document.getElementById('extra') !== null || document.getElementById('not-found') !== null`);
    const extraFound = await exists('#extra #extra-child');
    console.log(`      /extra/3 (separate IsTrimmable assembly, reflection only): ${extraFound ? 'FOUND' : 'NOT FOUND'}`);
    check(`pages: the Extra assembly's nested pages are ${expectExtra ? 'found' : 'gone (trimmed, no TrimmerRootAssembly)'}`, [extraFound, await docTitle()], expectExtra ? [true, 'Extra child - Router sample'] : [false, 'Not found']);

    await open('/trim-probe', 'pages', `document.getElementById('trim-probe') !== null || document.getElementById('not-found') !== null`);
    check('pages: an unreferenced page of the app assembly is found (the default publish keeps the app assembly)', [await exists('#trim-probe'), await docTitle()], [true, 'Trim probe - Router sample']);
    check('pages: clean console', consoleProblems(), []);
  }
} catch (error) {
  failures.push('script error: ' + error.message);
  console.log('ERROR ' + error.message + '\n' + page.messages.slice(-10).join('\n'));
} finally {
  await page.close();
}

console.log(`\n${checks - failures.length}/${checks} checks passed${failures.length ? '; failed: ' + failures.join('; ') : ''}`);
process.exit(failures.length ? 1 : 0);
