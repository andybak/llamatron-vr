const buildId = '__LLAMATRON_BUILD_ID__';
const scope = self.registration.scope;
const cachePrefix = `llamatron:${scope}:`;
const cacheName = `${cachePrefix}${buildId}`;
const completeUrl = new URL(`.offline-complete-${buildId}`, scope).href;
const clientVersions = new Map();
const legacyName = /^llamatron-\d{14}$/;

async function offlineReady() {
  try { return !!(await (await caches.open(cacheName)).match(completeUrl)); }
  catch { return false; }
}
async function cacheBundle() {
  if (await offlineReady()) return;
  const response = await fetch(new URL('precache.json', scope), { cache: 'no-store' });
  if (!response.ok) throw new Error('Offline asset list unavailable');
  const manifest = await response.json();
  if (manifest.buildId !== buildId) throw new Error('Deployment changed during offline installation');
  const cache = await caches.open(cacheName);
  const requests = manifest.files.map(path => {
    const url = new URL(path, scope);
    const html = url.href === scope || url.pathname.endsWith('/index.html');
    return new Request(url, { cache: html ? 'no-store' : 'default' });
  });
  await cache.addAll(requests);
  const index = await cache.match(new URL('index.html', scope).href);
  if (!index || !(await index.text()).includes(`name="llamatron-build" content="${buildId}"`))
    throw new Error('Offline page does not match its build');
  await cache.put(completeUrl, new Response(buildId));
}
self.addEventListener('install', event => {
  event.waitUntil((async () => {
    try { await cacheBundle(); }
    catch (error) {
      console.warn('[LLUPDATE] Offline installation unavailable', error);
      // A quota/network failure must not leave the old cache-first worker in
      // control. Preserve older complete bundles and still fix navigation.
      try { if (!(await offlineReady())) await caches.delete(cacheName); }
      catch { /* Storage can be unavailable; online navigation still works. */ }
    }
    await self.skipWaiting();
  })());
});
async function scopedClients() {
  return (await self.clients.matchAll({ type: 'window', includeUncontrolled: true }))
    .filter(client => client.url.startsWith(scope));
}
async function pruneUnusedCaches() {
  if (!(await offlineReady())) return;
  const clients = await scopedClients();
  if (clients.some(client => clientVersions.get(client.id) !== buildId)) return;
  for (const key of await caches.keys()) {
    if (key === cacheName) continue;
    if (key.startsWith(cachePrefix) && key.slice(cachePrefix.length) < buildId) await caches.delete(key);
    else if (legacyName.test(key) && key.slice('llamatron-'.length) < buildId) {
      const requests = await (await caches.open(key)).keys();
      if (requests.length && requests.every(request => request.url.startsWith(scope))) await caches.delete(key);
    }
  }
}
async function tellVersion(client) {
  client.postMessage({ type: 'BUILD_AVAILABLE', buildId, offlineReady: await offlineReady() });
}
self.addEventListener('activate', event => {
  event.waitUntil((async () => {
    await self.clients.claim();
    for (const client of await scopedClients()) {
      await tellVersion(client);
      client.postMessage({ type: 'REQUEST_CLIENT_VERSION' });
    }
    // Old clients keep their assets until they reload/report the new version.
  })());
});
self.addEventListener('message', event => {
  const client = event.source;
  if (!client || !client.url.startsWith(scope)) return;
  if (event.data?.type === 'GET_VERSION') event.waitUntil(tellVersion(client));
  else if (event.data?.type === 'CLIENT_VERSION') {
    clientVersions.set(client.id, event.data.buildId);
    event.waitUntil(pruneUnusedCaches());
  }
});
async function cachedAsset(request) {
  try {
    const current = await (await caches.open(cacheName)).match(request);
    if (current) return current;
    for (const key of (await caches.keys()).reverse()) {
      if (!key.startsWith(cachePrefix) && !legacyName.test(key)) continue;
      const cached = await (await caches.open(key)).match(request);
      if (cached) return cached;
    }
  } catch (error) { console.warn('[LLUPDATE] Asset cache unavailable', error); }
}
async function offlinePage() {
  try {
    const indexUrl = new URL('index.html', scope).href;
    if (await offlineReady()) return (await caches.open(cacheName)).match(indexUrl);
    for (const key of (await caches.keys()).reverse()) {
      if (!key.startsWith(cachePrefix) && !legacyName.test(key)) continue;
      const cache = await caches.open(key);
      const version = key.slice(cachePrefix.length);
      if (!legacyName.test(key) && !(await cache.match(new URL(`.offline-complete-${version}`, scope).href))) continue;
      const index = await cache.match(indexUrl);
      if (index) return index;
    }
  } catch (error) { console.warn('[LLUPDATE] Offline cache unavailable', error); }
}
self.addEventListener('fetch', event => {
  const request = event.request;
  if (request.method !== 'GET' || !request.url.startsWith(scope)) return;
  event.respondWith((async () => {
    if (request.mode === 'navigate') {
      try {
        const response = await fetch(request, { cache: 'no-store' });
        if (response.ok) return response;
        return (await offlinePage()) || response;
      } catch {
        return (await offlinePage()) || new Response('Connect to the internet to load the game.', { status: 503 });
      }
      // Navigation is never put into an older build's bundle: offline HTML and
      // its Unity data/code must always come from the same completed install.
    }
    const cached = await cachedAsset(request);
    if (cached) return cached;
    const response = await fetch(request);
    if (response.ok) {
      try { await (await caches.open(cacheName)).put(request, response.clone()); }
      catch (error) { console.warn('[LLUPDATE] Asset cache unavailable', error); }
    }
    return response;
  })());
});
