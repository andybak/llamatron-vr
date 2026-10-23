mergeInto(LibraryManager.library, {
  LLWebGameStarted: function () {
    window.dispatchEvent(new CustomEvent('llamatron-started'));
  },
  LLWebImportResult: function (success) {
    window.dispatchEvent(new CustomEvent('llamatron-import-result', { detail: !!success }));
  },
  LLWebUpdateState: function (data) {
    window.dispatchEvent(new CustomEvent('llamatron-update-state', { detail: UTF8ToString(data) }));
  },
  LLWebSaveNotice: function (message) {
    window.dispatchEvent(new CustomEvent('llamatron-save', { detail: UTF8ToString(message) }));
  },
  LLWebDownloadSave: function (data) {
    var binary = atob(UTF8ToString(data));
    var bytes = new Uint8Array(binary.length);
    for (var i = 0; i < binary.length; i++) bytes[i] = binary.charCodeAt(i);
    var url = URL.createObjectURL(new Blob([bytes], { type: 'application/octet-stream' }));
    var link = document.createElement('a');
    link.href = url; link.download = 'llamatron.llsave';
    document.body.appendChild(link); link.click(); link.remove();
    setTimeout(function () { URL.revokeObjectURL(url); }, 60000);
  },
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
