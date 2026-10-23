import test from 'node:test';
import assert from 'node:assert/strict';
import vm from 'node:vm';
import { readFileSync } from 'node:fs';
const template = 'standalone/Unity/Assets/WebGLTemplates/Llamatron/';
const source = readFileSync(`${template}service-worker.js`, 'utf8');
const updaterSource = readFileSync(`${template}updates.js`, 'utf8');
const scope = 'https://example.test/llamatron-vr/';
const A = '20261023010000', B = '20261023020000', C = '20261023030000';
const prefix = `llamatron:${scope}:`;
const html = version => `<meta name="llamatron-build" content="${version}">`;
const urlOf = value => value.url || value.href || new URL(value, scope).href;
class MemoryCache {
  constructor(world) { this.world = world; this.entries = new Map(); }
  async match(request) { return this.entries.get(urlOf(request))?.clone(); }
  async put(request, response) { this.entries.set(urlOf(request), response.clone()); }
  async keys() { return [...this.entries.keys()].map(url => new Request(url)); }
  async addAll(requests) {
    if (this.world.failPrecache) throw new Error('quota');
    for (const request of requests) await this.put(request, await this.world.fetch(request));
  }
}
function world() {
  const state = { stores: new Map(), remoteVersion: B, offline: false, failPrecache: false, cacheDisabled: false, calls: [], clients: [] };
  state.fetch = async (request, options) => {
    state.calls.push({ url: urlOf(request), cache: options?.cache || request.cache });
    if (state.offline) throw new Error('offline');
    const url = new URL(urlOf(request));
    if (url.pathname.endsWith('/precache.json')) return Response.json({ buildId: state.remoteVersion, files: ['./', 'index.html', `Build/${state.remoteVersion}.wasm`] });
    if (url.href === scope || url.pathname.endsWith('/index.html')) return new Response(html(state.remoteVersion));
    return new Response(`asset:${state.remoteVersion}`);
  };
  state.caches = {
    async open(key) {
      if (state.cacheDisabled) throw new Error('cache disabled');
      if (!state.stores.has(key)) state.stores.set(key, new MemoryCache(state));
      return state.stores.get(key);
    },
    async keys() { if (state.cacheDisabled) throw new Error('cache disabled'); return [...state.stores.keys()]; },
    async delete(key) { return state.stores.delete(key); }
  };
  return state;
}
function client(id) { return { id, url: scope, messages: [], postMessage(message) { this.messages.push(message); } }; }
function worker(state, version) {
  const handlers = {}; let skipped = false, claimed = false;
  const self = { registration: { scope }, addEventListener: (name, callback) => { handlers[name] = callback; },
    skipWaiting: async () => { skipped = true; }, clients: { matchAll: async () => state.clients, claim: async () => { claimed = true; } } };
  vm.runInNewContext(source.replaceAll('__LLAMATRON_BUILD_ID__', version), {
    self, caches: state.caches, fetch: state.fetch, URL, Request, Response, console: { warn() {} }, Map
  });
  return {
    get skipped() { return skipped; }, get claimed() { return claimed; },
    async fire(name, detail = {}) {
      const pending = []; let response;
      handlers[name]({ ...detail, waitUntil: promise => pending.push(promise), respondWith: promise => { response = promise; } });
      await Promise.all(pending); return response ? await response : undefined;
    }
  };
}
async function seed(state, version, legacy = false) {
  const cache = await state.caches.open(legacy ? `llamatron-${version}` : `${prefix}${version}`);
  await cache.put(`${scope}index.html`, new Response(html(version)));
  await cache.put(`${scope}Build/${version}.wasm`, new Response(`old:${version}`));
  if (!legacy) await cache.put(`${scope}.offline-complete-${version}`, new Response(version));
}
const navigation = { url: scope, method: 'GET', mode: 'navigate' };

test('normal navigation gets new HTML; offline navigation retains a complete matching build', async () => {
  const state = world(); await seed(state, A, true); state.clients = [client('old')];
  const active = worker(state, B); await active.fire('install'); await active.fire('activate');
  assert.ok(active.skipped && active.claimed);
  assert.equal(await (await active.fire('fetch', { request: navigation })).text(), html(B));
  assert.equal(state.calls.at(-1).cache, 'no-store');
  state.remoteVersion = C;
  assert.equal(await (await active.fire('fetch', { request: navigation })).text(), html(C));
  state.offline = true;
  assert.equal(await (await active.fire('fetch', { request: navigation })).text(), html(B));
});
test('running older pages keep their assets; cleanup waits for all clients and preserves newer installs/other apps', async () => {
  const state = world(); await seed(state, A); await seed(state, A, true);
  await state.caches.open('unrelated-app'); await state.caches.open(`${prefix}${C}`);
  const otherLegacy = await state.caches.open('llamatron-20251001000000');
  await otherLegacy.put('https://example.test/other-app/index.html', new Response('other'));
  const tab = client('one'); state.clients = [tab];
  const active = worker(state, B); await active.fire('install'); await active.fire('activate');
  await active.fire('message', { source: tab, data: { type: 'CLIENT_VERSION', buildId: A } });
  assert.ok(state.stores.has(`${prefix}${A}`));
  state.offline = true;
  const oldAsset = { url: `${scope}Build/${A}.wasm`, method: 'GET', mode: 'cors' };
  assert.equal(await (await active.fire('fetch', { request: oldAsset })).text(), `old:${A}`);
  await active.fire('message', { source: tab, data: { type: 'CLIENT_VERSION', buildId: B } });
  assert.ok(!state.stores.has(`${prefix}${A}`));
  assert.ok(!state.stores.has(`llamatron-${A}`));
  assert.ok(state.stores.has(`${prefix}${C}`));
  assert.ok(state.stores.has('unrelated-app'));
  assert.ok(state.stores.has('llamatron-20251001000000'));
});
test('failed precaching still replaces the stale worker and keeps an older complete offline bundle', async () => {
  const state = world(); await seed(state, A); state.failPrecache = true;
  const active = worker(state, B); await active.fire('install'); await active.fire('activate');
  assert.ok(active.skipped && active.claimed);
  assert.equal(await (await active.fire('fetch', { request: navigation })).text(), html(B));
  state.offline = true;
  assert.equal(await (await active.fire('fetch', { request: navigation })).text(), html(A));
});
test('blocked cache storage does not break online navigation or assets', async () => {
  const state = world(); state.cacheDisabled = true;
  const active = worker(state, B); await active.fire('install'); await active.fire('activate');
  assert.ok(active.skipped && active.claimed);
  assert.equal(await (await active.fire('fetch', { request: navigation })).text(), html(B));
  assert.equal(await (await active.fire('fetch', { request: { url: `${scope}Build/${B}.wasm`, method: 'GET', mode: 'cors' } })).text(), `asset:${B}`);
});
function updaterEnvironment({ capture = async () => 'portable-save', restore = async () => true, isPlaying = true } = {}) {
  const data = new Map([['manual-quick-slot', 'keep-me']]); let reloads = 0, registered;
  const errors = [], listeners = {};
  const storage = { getItem: key => data.get(key) || null, setItem: (key, value) => data.set(key, value), removeItem: key => data.delete(key) };
  const registration = { update: async () => {} };
  const navigator = { onLine: true, serviceWorker: { controller: { postMessage() {} },
    addEventListener: (name, callback) => { listeners[name] = callback; },
    register: async (path, options) => { registered = { path, options }; return registration; } } };
  const sandbox = { sessionStorage: storage, navigator, document: { visibilityState: 'visible', addEventListener() {} },
    window: { isSecureContext: true, addEventListener() {}, location: { reload: () => { reloads++; } } },
    setInterval() {}, console: { warn() {} } };
  vm.runInNewContext(updaterSource, sandbox);
  const create = () => new sandbox.LlamatronUpdater({ buildId: A, isPlaying: () => isPlaying, captureState: capture,
    restoreState: restore, onError: message => errors.push(message), onAvailable() {} });
  return { create, data, storage, errors, get reloads() { return reloads; }, get registered() { return registered; } };
}
test('update handoff preserves the current game without overwriting the quick slot', async () => {
  const env = updaterEnvironment(); const updater = env.create(); await updater.start();
  assert.equal(env.registered.path, 'service-worker.js');
  assert.equal(env.registered.options.updateViaCache, 'none');
  assert.ok(await updater.apply()); assert.equal(env.reloads, 1);
  assert.equal(env.data.get('manual-quick-slot'), 'keep-me');
  let restored;
  const next = updaterEnvironment({ restore: async state => { restored = state; return true; } });
  next.data.set(updater.key, env.data.get(updater.key));
  const resumed = next.create(); assert.ok(resumed.hasResume);
  assert.ok(await resumed.restore()); assert.equal(restored, 'portable-save');
  assert.ok(!next.data.has(updater.key));
});
test('capture/storage failures cancel updating; failed restoration retains a recoverable save', async () => {
  const failed = updaterEnvironment({ capture: async () => { throw new Error('capture failed'); } });
  assert.equal(await failed.create().apply(), false); assert.equal(failed.reloads, 0);
  const quota = updaterEnvironment(); quota.storage.setItem = () => { throw new Error('quota'); };
  assert.equal(await quota.create().apply(), false); assert.equal(quota.reloads, 0);
  const recovery = updaterEnvironment({ restore: async () => false });
  recovery.data.set('llamatron-update-resume-v1', JSON.stringify({ state: 'keep-this-save' }));
  const resume = recovery.create(); assert.equal(await resume.restore(), false);
  assert.ok(resume.hasResume && recovery.data.has(resume.key));
});
