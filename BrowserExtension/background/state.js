(function (root) {
  const PD = root.PD || (root.PD = {});

  let interceptCount = 0;

  PD.State = {
    getInterceptCount: () => interceptCount,
    resetInterceptCount() { interceptCount = 0; },
    incrementInterceptCount() { interceptCount++; return interceptCount; },
    cdCache: new Map(),
    hlsManifestsByTab: new Map()
  };
})(self);
