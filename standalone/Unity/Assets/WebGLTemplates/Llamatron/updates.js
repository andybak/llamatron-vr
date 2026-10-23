/* Update policy is separate from Unity startup so it can be tested without a player. */
class LlamatronUpdater {
  constructor(options) {
    this.options = options;
    this.version = options.buildId;
    this.key = 'llamatron-update-resume-v1';
    this.pending = null;
    try {
      const stored = sessionStorage.getItem(this.key);
      if (stored) {
        const pending = JSON.parse(stored);
        if (typeof pending.state !== 'string' || pending.state.length > 3 * 1024 * 1024) throw new Error('Invalid retained update save');
        this.pending = pending;
      }
    } catch (error) { console.warn('[LLUPDATE] Resume storage unavailable', error); }
  }
  get hasResume() { return !!this.pending?.state; }
  async start() {
    if (!('serviceWorker' in navigator) || !window.isSecureContext) return;
    navigator.serviceWorker.addEventListener('message', event => {
      if (event.data?.type === 'REQUEST_CLIENT_VERSION') this.announce();
      else if (event.data?.type === 'BUILD_AVAILABLE' && /^\d{14}$/.test(event.data.buildId)) {
        if (event.data.buildId > this.version) {
          this.available = event.data;
          this.options.onAvailable();
        }
      }
    });
    navigator.serviceWorker.addEventListener('controllerchange', () => this.announce());
    this.registration = await navigator.serviceWorker.register('service-worker.js', { updateViaCache: 'none' });
    const check = () => this.check().catch(error => console.warn('[LLUPDATE] Update check unavailable', error));
    document.addEventListener('visibilitychange', () => { if (document.visibilityState === 'visible') check(); });
    window.addEventListener('online', check);
    setInterval(() => { if (document.visibilityState === 'visible') check(); }, 60000);
    await this.check();
  }
  announce() {
    const worker = navigator.serviceWorker.controller;
    worker?.postMessage({ type: 'CLIENT_VERSION', buildId: this.version });
    worker?.postMessage({ type: 'GET_VERSION' });
  }
  async check() {
    this.announce();
    await this.registration.update();
  }
  async apply() {
    try {
      if (navigator.onLine === false && !this.available?.offlineReady)
        throw new Error('Connect to the internet to apply this update.');
      if (this.options.isPlaying()) {
        const state = await this.options.captureState();
        if (!state) throw new Error('The current game could not be preserved.');
        // Keep the update handoff separate from the user's manual quick slot.
        const pending = { fromBuild: this.version, state };
        sessionStorage.setItem(this.key, JSON.stringify(pending));
        this.pending = pending;
      }
      window.location.reload();
      return true;
    } catch (error) {
      this.options.onError(`Update cancelled: ${error.message}`);
      return false;
    }
  }
  async restore() {
    if (!this.hasResume) return true;
    try {
      if (!(await this.options.restoreState(this.pending.state))) throw new Error('The previous game could not be restored.');
      sessionStorage.removeItem(this.key); this.pending = null;
      return true;
    } catch (error) {
      this.options.onError(`${error.message} A copy is retained; download it before closing this tab.`);
      return false;
    }
  }
}
globalThis.LlamatronUpdater = LlamatronUpdater;
