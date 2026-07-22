// Cross-browser WebExtensions API adapter.
//
// Firefox exposes Promise-based APIs through `browser.*` and keeps
// callback-compatible `chrome.*` aliases for porting. Chromium Manifest V3
// exposes `chrome.*` with Promise support for most asynchronous methods while
// preserving callbacks. PDownloader uses both styles, so this proxy selects
// the right native namespace per invocation.
(() => {
  if (globalThis.PDWebExt) return;

  const promiseApi = globalThis.browser ?? null;
  const callbackApi = globalThis.chrome ?? promiseApi;

  if (!promiseApi && !callbackApi) {
    throw new Error('PDownloader WebExtensions API is not available in this context.');
  }

  const proxyCache = new WeakMap();

  function getCachedProxy(promiseObject, callbackObject) {
    const cacheKey = promiseObject || callbackObject;
    if (!cacheKey || (typeof cacheKey !== 'object' && typeof cacheKey !== 'function')) {
      return null;
    }

    let byCallback = proxyCache.get(cacheKey);
    if (!byCallback) {
      byCallback = new WeakMap();
      proxyCache.set(cacheKey, byCallback);
    }

    const callbackKey = callbackObject && (typeof callbackObject === 'object' || typeof callbackObject === 'function')
      ? callbackObject
      : cacheKey;

    return byCallback.get(callbackKey) || null;
  }

  function setCachedProxy(promiseObject, callbackObject, proxy) {
    const cacheKey = promiseObject || callbackObject;
    if (!cacheKey || (typeof cacheKey !== 'object' && typeof cacheKey !== 'function')) {
      return;
    }

    let byCallback = proxyCache.get(cacheKey);
    if (!byCallback) {
      byCallback = new WeakMap();
      proxyCache.set(cacheKey, byCallback);
    }

    const callbackKey = callbackObject && (typeof callbackObject === 'object' || typeof callbackObject === 'function')
      ? callbackObject
      : cacheKey;

    byCallback.set(callbackKey, proxy);
  }

  function createApiProxy(promiseObject, callbackObject) {
    const cached = getCachedProxy(promiseObject, callbackObject);
    if (cached) return cached;

    const proxy = new Proxy(Object.create(null), {
      get(_target, property) {
        if (property === '__pdPromiseApi') return promiseObject;
        if (property === '__pdCallbackApi') return callbackObject;

        const promiseValue = promiseObject?.[property];
        const callbackValue = callbackObject?.[property];

        if (typeof promiseValue === 'function' || typeof callbackValue === 'function') {
          return (...args) => {
            // Existing PDownloader code intentionally uses callbacks in a few
            // places. Firefox's `chrome.*` compatibility namespace supports
            // those callbacks, while `browser.*` is the Promise-first API.
            const hasCallback = typeof args.at(-1) === 'function';

            if (hasCallback && typeof callbackValue === 'function') {
              return callbackValue.apply(callbackObject, args);
            }

            if (typeof promiseValue === 'function') {
              return promiseValue.apply(promiseObject, args);
            }

            return callbackValue.apply(callbackObject, args);
          };
        }

        const promiseIsObject = promiseValue && (typeof promiseValue === 'object' || typeof promiseValue === 'function');
        const callbackIsObject = callbackValue && (typeof callbackValue === 'object' || typeof callbackValue === 'function');

        if (promiseIsObject || callbackIsObject) {
          return createApiProxy(
            promiseIsObject ? promiseValue : null,
            callbackIsObject ? callbackValue : null
          );
        }

        return promiseValue !== undefined ? promiseValue : callbackValue;
      },

      has(_target, property) {
        return property in (promiseObject || {}) || property in (callbackObject || {});
      }
    });

    setCachedProxy(promiseObject, callbackObject, proxy);
    return proxy;
  }

  const isFirefox = !!globalThis.browser?.runtime?.getBrowserInfo;

  globalThis.PDWebExtPlatform = Object.freeze({
    isFirefox,
    isChromium: !isFirefox
  });

  globalThis.PDWebExtCompat = Object.freeze({
    webRequestExtraInfoSpec(...items) {
      // Chromium supports the `extraHeaders` enum value. Firefox rejects it
      // with NS_ERROR_INVALID_ARG / Invalid enumeration value. Keep the
      // strongest available header visibility on each browser without
      // preventing the background runtime from starting.
      return isFirefox
        ? items.filter(item => item !== 'extraHeaders')
        : items;
    }
  });

  globalThis.PDWebExt = createApiProxy(promiseApi, callbackApi);
})();
