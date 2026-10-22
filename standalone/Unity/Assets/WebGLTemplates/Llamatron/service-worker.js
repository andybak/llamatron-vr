const cacheName = 'llamatron-__LLAMATRON_BUILD_ID__';
self.addEventListener('install', event => {
  event.waitUntil(fetch('precache.json', { cache: 'no-store' })
    .then(response => { if (!response.ok) throw new Error('Offline asset list unavailable'); return response.json(); })
    .then(manifest => caches.open(cacheName).then(cache => cache.addAll(manifest.files))));
});
self.addEventListener('activate', event => {
  event.waitUntil(caches.keys().then(keys => Promise.all(keys
    .filter(key => key.startsWith('llamatron-') && key !== cacheName)
    .map(key => caches.delete(key)))).then(() => self.clients.claim()));
});
self.addEventListener('fetch', event => {
  const url = new URL(event.request.url);
  if (event.request.method !== 'GET' || url.origin !== self.location.origin) return;
  event.respondWith(caches.open(cacheName).then(async cache => {
    const cached = await cache.match(event.request);
    if (cached) return cached;
    const response = await fetch(event.request);
    if (response.ok) {
      try { await cache.put(event.request, response.clone()); }
      catch (error) { console.warn('Offline cache unavailable', error); }
    }
    return response;
  }));
});
