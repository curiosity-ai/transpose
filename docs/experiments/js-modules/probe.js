// Renders the Tesserae sample app and captures a structural fingerprint of the page,
// so a modular build can be diffed against the single-bundle baseline.
//
//   node probe.js <siteDir> <outPrefix> [port]
const { chromium } = require('/opt/node22/lib/node_modules/playwright');
const http = require('http');
const fs = require('fs');
const path = require('path');

const siteDir = process.argv[2];
const outPrefix = process.argv[3];
const port = parseInt(process.argv[4] || '5199', 10);

const MIME = {
  '.html': 'text/html', '.js': 'text/javascript', '.mjs': 'text/javascript',
  '.css': 'text/css', '.json': 'application/json', '.svg': 'image/svg+xml',
  '.png': 'image/png', '.jpg': 'image/jpeg', '.woff2': 'font/woff2',
  '.woff': 'font/woff', '.ttf': 'font/ttf', '.gif': 'image/gif', '.ico': 'image/x-icon',
};

function serve() {
  return new Promise(resolve => {
    const server = http.createServer((req, res) => {
      let p = decodeURIComponent(req.url.split('?')[0]);
      if (p === '/') p = '/index.html';
      const file = path.join(siteDir, p);
      if (!file.startsWith(siteDir) || !fs.existsSync(file) || fs.statSync(file).isDirectory()) {
        res.writeHead(404); res.end('nope'); return;
      }
      res.writeHead(200, { 'Content-Type': MIME[path.extname(file)] || 'application/octet-stream' });
      fs.createReadStream(file).pipe(res);
    });
    server.listen(port, () => resolve(server));
  });
}

(async () => {
  const server = await serve();
  const browser = await chromium.launch({ executablePath: '/opt/pw-browsers/chromium-1194/chrome-linux/chrome' });
  const ctx = await browser.newContext({ viewport: { width: 1400, height: 1000 } });
  const page = await ctx.newPage();

  const console_ = [], errors = [], requests = [];
  page.on('console', m => console_.push(`${m.type()}: ${m.text()}`));
  page.on('pageerror', e => errors.push(String(e)));
  page.on('request', r => requests.push(r.url().replace(`http://localhost:${port}`, '')));

  const t0 = Date.now();
  await page.goto(`http://localhost:${port}/index.html`, { waitUntil: 'networkidle', timeout: 60000 });
  await page.waitForTimeout(2500);
  const loadMs = Date.now() - t0;

  // The sample gallery: the sidebar is built from the reflection-discovered ISample types.
  const fingerprint = await page.evaluate(() => {
    const norm = s => (s || '').replace(/\s+/g, ' ').trim();
    const sidebar = [...document.querySelectorAll('.tss-sidebar-item, [class*=sidebar] [class*=item]')]
      .map(e => norm(e.textContent)).filter(Boolean);
    return {
      title: document.title,
      bodyTextLen: norm(document.body.innerText).length,
      elementCount: document.querySelectorAll('*').length,
      sidebarCount: sidebar.length,
      sidebar: sidebar.slice(0, 400),
      // How many types the runtime believes the tss + app assemblies contain.
      tssTypes: (() => { try { return Object.keys(System.Reflection.Assembly.assemblies['tss'].$types).length; } catch { return -1; } })(),
      appTypes: (() => { try { return Object.keys(System.Reflection.Assembly.assemblies['Tesserae.Tests'].$types).length; } catch { return -1; } })(),
    };
  });

  // Click through every sidebar entry and fingerprint the rendered sample.
  const samples = [];
  const items = await page.$$('[class*=sidebar] [class*=item]');
  for (let i = 0; i < items.length; i++) {
    const els = await page.$$('[class*=sidebar] [class*=item]');
    if (i >= els.length) break;
    let label = '';
    try { label = ((await els[i].innerText()) || '').replace(/\s+/g, ' ').trim(); } catch { }
    if (!label) continue;
    try {
      await els[i].click({ timeout: 3000 });
      await page.waitForTimeout(220);
      const s = await page.evaluate(() => {
        const main = document.querySelector('[class*=content], main') || document.body;
        return { n: main.querySelectorAll('*').length, t: (main.innerText || '').replace(/\s+/g, ' ').trim().length };
      });
      samples.push({ label, ...s });
    } catch (e) {
      samples.push({ label, error: String(e).slice(0, 120) });
    }
  }

  await page.screenshot({ path: `${outPrefix}.png`, fullPage: false });
  const report = { loadMs, fingerprint, samples, errors, console: console_.filter(c => !c.startsWith('log:')).slice(0, 60), requestCount: requests.length };
  fs.writeFileSync(`${outPrefix}.json`, JSON.stringify(report, null, 2));

  console.log(`load ${loadMs}ms  elements=${fingerprint.elementCount}  sidebar=${fingerprint.sidebarCount}  tssTypes=${fingerprint.tssTypes} appTypes=${fingerprint.appTypes}`);
  console.log(`samples clicked: ${samples.length}, errors: ${errors.length}, requests: ${requests.length}`);
  if (errors.length) console.log('ERRORS:\n' + errors.slice(0, 10).join('\n'));

  await browser.close();
  server.close();
})();
