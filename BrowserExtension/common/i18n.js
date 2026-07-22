(function (root) {
  const PD = root.PD || (root.PD = {});

  function t(key, substitutions) {
    try {
      return PDWebExt.i18n.getMessage(key, substitutions) || key;
    } catch (_) {
      return key;
    }
  }

  function applyToDom(root2) {
    const scope = root2 || document;

    scope.querySelectorAll('[data-i18n]').forEach(el => {
      el.textContent = t(el.getAttribute('data-i18n'));
    });
    scope.querySelectorAll('[data-i18n-placeholder]').forEach(el => {
      el.setAttribute('placeholder', t(el.getAttribute('data-i18n-placeholder')));
    });
    scope.querySelectorAll('[data-i18n-title]').forEach(el => {
      el.setAttribute('title', t(el.getAttribute('data-i18n-title')));
    });
  }

  PD.I18n = { t, applyToDom };
})(typeof self !== 'undefined' ? self : this);
