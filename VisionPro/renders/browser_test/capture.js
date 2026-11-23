// Renders real pages the way the Workspace browser would show them (approximation: Chromium, not
// WebKit): viewport = the web view's size in points, 2x pixels, dark mode, Polish, iPhone Safari UA,
// and the same tracker hosts blocked as CardboardXRBrowser.mm's content rules.
const { chromium } = require('playwright');
const out = process.argv[2];

const TRACKERS = ['doubleclick.net', 'googlesyndication.com', 'googleadservices.com', 'google-analytics.com',
  'googletagmanager.com', 'adservice.google.com', 'connect.facebook.net', 'scorecardresearch.com', 'criteo.com',
  'criteo.net', 'taboola.com', 'outbrain.com', 'adnxs.com', 'hotjar.com', 'quantserve.com', 'amazon-adsystem.com',
  'moatads.com', 'rubiconproject.com', 'pubmatic.com', 'openx.net', 'casalemedia.com', 'smartadserver.com',
  'gemius.pl', 'adocean.pl'];

const PAGES = [
  ['duckduckgo', 'https://duckduckgo.com/'],
  ['wikipedia', 'https://pl.wikipedia.org/wiki/Rzeczywisto%C5%9B%C4%87_wirtualna'],
  ['onet', 'https://www.onet.pl/'],
  ['wp', 'https://www.wp.pl/'],
  ['allegro', 'https://allegro.pl/'],
  ['youtube', 'https://m.youtube.com/'],
  ['github', 'https://github.com/'],
  ['messenger', 'https://www.messenger.com/'],
];

// Window default: text area 460 x 220 dp -> web view 460 x 220 pt. Stretched to 1000 x 640 dp -> 940 x 500 pt.
const SIZES = [['domyslne', 460, 220], ['rozciagniete', 940, 500]];

const UA = 'Mozilla/5.0 (iPhone; CPU iPhone OS 18_1 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/18.1 Mobile/15E148 Safari/604.1';

(async () => {
  const browser = await chromium.launch();
  const results = [];
  for (const [name, url] of PAGES) {
    for (const [sizeName, width, height] of SIZES) {
      const context = await browser.newContext({
        viewport: { width, height }, deviceScaleFactor: 2, isMobile: true, hasTouch: true,
        colorScheme: 'dark', locale: 'pl-PL', userAgent: UA,
      });
      let blocked = 0;
      await context.route('**/*', route => {
        const host = new URL(route.request().url()).hostname;
        if (TRACKERS.some(t => host === t || host.endsWith('.' + t))) { blocked++; return route.abort(); }
        return route.continue();
      });
      const page = await context.newPage();
      const started = Date.now();
      let status = 'ok';
      try {
        const response = await page.goto(url, { waitUntil: 'domcontentloaded', timeout: 25000 });
        await page.waitForTimeout(3500);
        status = response ? String(response.status()) : 'no response';
      } catch (e) {
        status = 'error: ' + e.message.split('\n')[0];
      }
      const file = `${out}/${name}_${sizeName}.png`;
      try { await page.screenshot({ path: file }); } catch (e) { status += ' (no screenshot)'; }
      // What covers the page: big fixed elements (cookie banners, app prompts, login walls)
      let overlay = null;
      try {
        overlay = await page.evaluate(() => {
          const vw = innerWidth, vh = innerHeight; let best = null;
          for (const el of document.querySelectorAll('body *')) {
            const s = getComputedStyle(el);
            if (s.position !== 'fixed' && s.position !== 'sticky') continue;
            if (s.display === 'none' || s.visibility === 'hidden' || +s.opacity === 0) continue;
            const r = el.getBoundingClientRect();
            const area = Math.max(0, Math.min(r.right, vw) - Math.max(r.left, 0)) * Math.max(0, Math.min(r.bottom, vh) - Math.max(r.top, 0));
            const share = area / (vw * vh);
            if (share > 0.25 && (!best || share > best.share))
              best = { share: Math.round(share * 100), tag: el.tagName.toLowerCase(), id: el.id, cls: String(el.className).slice(0, 60),
                       text: (el.innerText || '').replace(/\s+/g, ' ').slice(0, 90) };
          }
          return best;
        });
      } catch (e) {}
      results.push({ name, size: sizeName, width, height, status, ms: Date.now() - started, blocked, overlay, file });
      console.log(`${name} ${sizeName}: ${status}, ${Date.now() - started} ms, trackers blocked: ${blocked}` +
        (overlay ? `, covered ${overlay.share}% by <${overlay.tag} id=${overlay.id} class=${overlay.cls}> "${overlay.text}"` : ''));
      await context.close();
    }
  }
  require('fs').writeFileSync(`${out}/results.json`, JSON.stringify(results, null, 2));
  await browser.close();
})();
