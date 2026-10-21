// Maxim SS Tools — industrial backdrop: blueprint grid, drifting line
// particles, slow vertical pulse bar, faint scanlines. Flat colors only.
(function () {
  var c = document.getElementById("bg");
  if (!c) return;
  var x = c.getContext("2d"), W, H;
  var reduce = window.matchMedia && matchMedia("(prefers-reduced-motion: reduce)").matches;
  var dark = true;
  function theme() { dark = document.documentElement.getAttribute("data-theme") !== "light"; }
  function resize() { W = c.width = window.innerWidth; H = c.height = window.innerHeight; }
  window.addEventListener("resize", resize);
  resize(); theme();
  try {
    new MutationObserver(theme).observe(document.documentElement,
      { attributes: true, attributeFilter: ["data-theme"] });
  } catch (e) {}
  var streaks = [];
  for (var i = 0; i < 55; i++) {
    streaks.push({ x: Math.random(), y: Math.random(),
      len: 6 + Math.random() * 16, v: 0.0004 + Math.random() * 0.0012 });
  }
  var pulse = 0;
  function paint(staticOnly) {
    x.clearRect(0, 0, W, H);
    x.strokeStyle = dark ? "rgba(120,160,220,0.08)" : "rgba(40,60,40,0.10)";
    x.lineWidth = 1;
    x.beginPath();
    for (var gx = 0.5; gx < W; gx += 28) { x.moveTo(gx, 0); x.lineTo(gx, H); }
    for (var gy = 0.5; gy < H; gy += 28) { x.moveTo(0, gy); x.lineTo(W, gy); }
    x.stroke();
    if (staticOnly) return;
    x.strokeStyle = dark ? "rgba(201,242,75,0.32)" : "rgba(77,124,15,0.35)";
    x.beginPath();
    for (var k = 0; k < streaks.length; k++) {
      var d = streaks[k];
      d.x += d.v;
      if (d.x > 1.02) d.x = -0.02;
      var px = d.x * W, py = d.y * H;
      x.moveTo(px, py); x.lineTo(px + d.len, py);
    }
    x.stroke();
    pulse = (pulse + 0.5) % (W + 160);
    x.fillStyle = dark ? "rgba(201,242,75,0.05)" : "rgba(77,124,15,0.06)";
    x.fillRect(pulse - 80, 0, 24, H);
    x.fillStyle = dark ? "rgba(0,0,0,0.10)" : "rgba(0,0,0,0.05)";
    for (var sy = 0; sy < H; sy += 4) x.fillRect(0, sy, W, 1);
  }
  if (reduce) { paint(true); return; }
  (function frame() { paint(false); requestAnimationFrame(frame); })();
})();
