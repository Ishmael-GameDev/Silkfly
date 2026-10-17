// Points the download button at the file for the visitor's system and shows the latest version.
(function () {
  "use strict";

  var button = document.getElementById("download");
  var list = document.getElementById("files");
  if (!button || !list) return;

  function detect() {
    var ua = navigator.userAgent;
    var platform = (navigator.userAgentData && navigator.userAgentData.platform) || navigator.platform || "";
    if (/Android|iPhone|iPad|iPod/i.test(ua)) return null;
    if (/Win/i.test(platform) || /Windows/i.test(ua)) return "windows";
    if (/Mac/i.test(platform) || /Mac OS X/i.test(ua)) return isIntelMac() ? "mac-intel" : "mac-arm";
    if (/Linux|X11|CrOS/i.test(platform + ua)) return "linux";
    return null;
  }

  // browsers don't tell the CPU; the GPU name does on Intel Macs, Apple Silicon is the default
  function isIntelMac() {
    try {
      var gl = document.createElement("canvas").getContext("webgl");
      var info = gl && gl.getExtension("WEBGL_debug_renderer_info");
      var renderer = info ? gl.getParameter(info.UNMASKED_RENDERER_WEBGL) : "";
      return /Intel/i.test(renderer);
    } catch (e) {
      return false;
    }
  }

  var os = detect();
  var item = os && list.querySelector('[data-os="' + os + '"]');
  if (item) {
    var link = item.querySelector("a");
    button.href = link.href;
    button.querySelector("span").textContent = item.getAttribute("data-button");
    item.classList.add("current");
  }

  var meta = document.getElementById("version");
  if (!meta || !window.fetch) return;
  fetch("https://api.github.com/repos/Ishmael-GameDev/Silkfly/releases/latest", {
    headers: { Accept: "application/vnd.github+json" },
  })
    .then(function (response) {
      return response.ok ? response.json() : null;
    })
    .then(function (release) {
      if (!release || !release.tag_name) return;
      meta.textContent = meta.getAttribute("data-template").replace("{v}", release.tag_name.replace(/^v/, ""));
      meta.hidden = false;
    })
    .catch(function () {});
})();
