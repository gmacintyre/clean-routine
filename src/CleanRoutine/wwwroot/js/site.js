// Shared helpers: anti-forgery token, toast, and refreshing when you come back to the app.
(function () {
  const tokenInput = document.querySelector('input[name="__RequestVerificationToken"]');
  window.cr = {
    token: tokenInput ? tokenInput.value : "",
    async post(url, data) {
      const body = data instanceof URLSearchParams ? data : new URLSearchParams(data || {});
      const res = await fetch(url, {
        method: "POST",
        headers: { RequestVerificationToken: this.token },
        body,
        credentials: "same-origin",
      });
      if (res.status === 401 || res.redirected) { location.href = "/Account/Login"; throw new Error("signed out"); }
      if (!res.ok) throw new Error("HTTP " + res.status);
      return res.headers.get("content-type")?.includes("json") ? res.json() : null;
    },
    toast(msg) {
      const t = document.getElementById("toast");
      t.textContent = msg;
      t.hidden = false;
      clearTimeout(t._timer);
      t._timer = setTimeout(() => { t.hidden = true; }, 4000);
    },
  };

  // Pick up the other person's changes when the app comes back to the front.
  let hiddenAt = 0;
  document.addEventListener("visibilitychange", () => {
    if (document.hidden) { hiddenAt = Date.now(); return; }
    if (hiddenAt && Date.now() - hiddenAt > 60_000 && document.body.dataset.refresh !== "off") location.reload();
  });

  // Submit small edit forms as soon as a field changes.
  document.addEventListener("change", (e) => {
    const el = e.target;
    if (el.matches("[data-autosubmit]")) el.form?.requestSubmit();
  });

  if ("serviceWorker" in navigator) navigator.serviceWorker.register("/sw.js").catch(() => {});
})();
