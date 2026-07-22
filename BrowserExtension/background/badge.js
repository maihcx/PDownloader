(function (root) {
  const PD = root.PD || (root.PD = {});

  function update() {
    const n = PD.State.getInterceptCount();
    if (n > 0) {
      PDWebExt.action.setBadgeText({ text: String(n) });
      PDWebExt.action.setBadgeBackgroundColor({ color: '#4FC3F7' });
    } else {
      PDWebExt.action.setBadgeText({ text: '' });
    }
  }

  PD.Badge = { update };
})(self);
