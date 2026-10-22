mergeInto(LibraryManager.library, {
  LLWebReady: function () {
    Module.llamatronResumeAudio = function () {
      if (typeof WEBAudio !== 'undefined' && WEBAudio.audioContext) {
        return WEBAudio.audioContext.resume();
      }
      return Promise.resolve();
    };
    window.dispatchEvent(new CustomEvent('llamatron-ready'));
  },
  LLWebStatus: function (supported, immersive) {
    window.dispatchEvent(new CustomEvent('llamatron-xr', {
      detail: { supported: !!supported, immersive: !!immersive }
    }));
  }
});
