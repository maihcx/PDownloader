(function (root) {
  const PD = root.PD || (root.PD = {});

  function init() { }

  function get(tabId) {
    if (tabId == null) return null;

    return PD.MediaCandidateRegistry?.getAll(tabId, {
      minScore: 80,
      includeSegments: false
    }).find(candidate => candidate.kind === 'hls' || candidate.kind === 'dash')
      || PD.State.hlsManifestsByTab.get(tabId)
      || null;
  }

  PD.HlsCapture = { init, get };
})(self);
