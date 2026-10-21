// Maxim SS Tools — console controller (WebView2 pywebview shim).
(function () {
  "use strict";
  var $ = function (id) { return document.getElementById(id); };
  var reduceMotion = window.matchMedia && matchMedia("(prefers-reduced-motion: reduce)").matches;

  /* ---------- sound: short square-wave mechanics, mutable ---------- */
  var Snd = {
    ctx: null,
    on: (function () { try { return localStorage.getItem("maxim-mute") !== "1"; } catch (e) { return true; } })(),
    ensure: function () {
      if (!this.ctx) { try { this.ctx = new (window.AudioContext || window.webkitAudioContext)(); } catch (e) {} }
      if (this.ctx && this.ctx.state === "suspended") this.ctx.resume();
    },
    tone: function (f, delay, dur, g) {
      if (!this.on) return;
      this.ensure();
      if (!this.ctx) return;
      try {
        var t = this.ctx.currentTime + (delay || 0);
        var o = this.ctx.createOscillator(), gn = this.ctx.createGain();
        o.type = "square"; o.frequency.value = f;
        gn.gain.setValueAtTime(g || 0.03, t);
        gn.gain.exponentialRampToValueAtTime(0.0001, t + (dur || 0.05));
        o.connect(gn); gn.connect(this.ctx.destination);
        o.start(t); o.stop(t + (dur || 0.05) + 0.01);
      } catch (e) {}
    },
    tick: function () { this.tone(2400, 0, 0.02, 0.015); },
    click: function () { this.tone(880, 0, 0.04, 0.04); this.tone(440, 0.03, 0.04, 0.03); },
    open: function () { this.tone(660, 0, 0.05, 0.035); this.tone(990, 0.05, 0.06, 0.035); },
    shut: function () { this.tone(990, 0, 0.05, 0.035); this.tone(660, 0.05, 0.06, 0.035); },
    arp: function () { this.tone(523, 0, 0.06, 0.04); this.tone(659, 0.07, 0.06, 0.04); this.tone(784, 0.14, 0.08, 0.04); },
    ok: function () { this.tone(784, 0, 0.07, 0.045); this.tone(1046, 0.08, 0.1, 0.045); },
    err: function () { this.tone(180, 0, 0.14, 0.05); }
  };

  /* ---------- backend bridge ---------- */
  function api(name) {
    var args = Array.prototype.slice.call(arguments, 1);
    if (!(window.pywebview && window.pywebview.api && window.pywebview.api[name])) {
      return Promise.reject(new Error("Backend not ready"));
    }
    return window.pywebview.api[name].apply(null, args);
  }
  function esc(s) {
    return String(s == null ? "" : s).replace(/[&<>"']/g, function (m) {
      return { "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" }[m];
    });
  }
  var toastTimer = null;
  function toast(msg) {
    var t = $("toast");
    t.textContent = msg;
    t.classList.remove("hidden");
    clearTimeout(toastTimer);
    toastTimer = setTimeout(function () { t.classList.add("hidden"); }, 3200);
  }
  function setBusy(btn, busy) { if (btn) btn.disabled = !!busy; }
  function cancel() { return api("cancel_scan").catch(function () {}); }

  /* ---------- theme / mute / clock / splash ---------- */
  function applyTheme(th) {
    document.documentElement.setAttribute("data-theme", th);
    try { localStorage.setItem("maxim-theme", th); } catch (e) {}
  }
  (function initTheme() {
    var th = "dark";
    try { th = localStorage.getItem("maxim-theme") || "dark"; } catch (e) {}
    applyTheme(th);
  })();
  $("btnTheme").addEventListener("click", function () {
    applyTheme(document.documentElement.getAttribute("data-theme") === "light" ? "dark" : "light");
  });
  function paintMute() { $("btnMute").classList.toggle("off", !Snd.on); $("btnMute").textContent = Snd.on ? "SND" : "MUTE"; }
  $("btnMute").addEventListener("click", function () {
    Snd.on = !Snd.on;
    try { localStorage.setItem("maxim-mute", Snd.on ? "0" : "1"); } catch (e) {}
    paintMute();
  });
  paintMute();
  setInterval(function () {
    var d = new Date(), p = function (n) { return (n < 10 ? "0" : "") + n; };
    $("clock").textContent = p(d.getHours()) + ":" + p(d.getMinutes()) + ":" + p(d.getSeconds());
  }, 1000);
  window.addEventListener("load", function () {
    setTimeout(function () { $("splash").classList.add("done"); }, reduceMotion ? 50 : 1100);
  });
  setTimeout(function () { $("splash").classList.add("done"); }, 4000); // shortcut: never trap

  /* ---------- navigation: one active page ---------- */
  var CRUMB = { overview: "OVERVIEW", services: "SERVICES", prefetch: "PREFETCH", bam: "BAM", alts: "ALTS", sysinfo: "SYSINFORMER", guide: "FIELD GUIDE", usn: "USN", ev: "EVERYTHING", time: "TIMELINE", check: "CHECKLIST" };
  function showPage(pg) {
    document.querySelectorAll(".page").forEach(function (x) { x.classList.remove("active"); });
    document.querySelectorAll("#dock button").forEach(function (x) { x.classList.toggle("active", x.dataset.pg === pg); });
    $("pg-" + pg).classList.add("active");
    $("crumb").textContent = CRUMB[pg] || pg.toUpperCase();
    $("main").scrollTop = 0;
    if (pg === "time") renderTimeline();
  }
  document.querySelectorAll("#dock button").forEach(function (b) {
    b.addEventListener("click", function () { Snd.click(); showPage(b.dataset.pg); });
    b.addEventListener("mouseenter", function () { Snd.tick(); });
  });
  document.querySelectorAll("[data-go]").forEach(function (b) {
    b.addEventListener("click", function () { Snd.click(); showPage(b.dataset.go); });
  });
  document.addEventListener("click", function (e) {
    var b = e.target.closest ? e.target.closest("button") : null;
    if (b && !b.dataset.pg && !b.dataset.go) Snd.click();
  });
  $("btnMin").addEventListener("click", function () { api("minimize_window").catch(toast); });
  $("btnClose").addEventListener("click", function () { api("close_window").catch(toast); });
  function relaunchAdmin() {
    api("relaunch_as_admin").then(function () { toast("RELAUNCHING AS ADMIN…"); })
      .catch(function (e) { toast(String((e && e.message) || e)); });
  }
  ["btnAdmin", "btnAdmin2", "btnAdmin3"].forEach(function (id) {
    var b = $(id);
    if (b) b.addEventListener("click", relaunchAdmin);
  });
  ["cancelServices", "cancelAlts", "cancelPrefetch", "cancelBam", "cancelUsn"].forEach(function (id) {
    $(id).addEventListener("click", function () { cancel().then(function () { toast("CANCEL REQUESTED"); }); });
  });
  document.addEventListener("keydown", function (e) {
    if (!$("modal").classList.contains("hidden") && e.key === "Escape") { closeModal(); return; }
    if (e.key === "Escape") { showPage("overview"); return; }
    if (e.key === "/" && document.activeElement !== $("gSearch") &&
        !/INPUT|TEXTAREA|SELECT/.test((document.activeElement || {}).tagName || "")) {
      e.preventDefault();
      showPage("guide");
      setTimeout(function () { $("gSearch").focus(); }, 60);
    }
  });

  /* ---------- admin state ---------- */
  var adminKnown = false, isAdmin = false;
  function setAdmin(known, admin) {
    adminKnown = known; isAdmin = admin;
    $("adminBanner").classList.toggle("hidden", !known || admin);
    var ab = $("btnAdmin");
    if (ab) ab.classList.toggle("hidden", known && admin);
    $("svcLock").classList.toggle("hidden", !known || admin);
    $("bamLock").classList.toggle("hidden", !known || admin);
    $("adminLine").textContent = !known ? "UNKNOWN" : (admin ? "ADMIN" : "LIMITED");
  }

  /* ---------- shared evidence widgets ---------- */
  function verdict(el, level, title, sub) {
    $(el).innerHTML = '<div class="verdict ' + level + '"><span>■</span><span>' + esc(title) +
      (sub ? ' <small>' + esc(sub) + "</small>" : "") + "</span></div>";
  }
  function tile(label, n, cls) {
    var num = typeof n === "number" || (typeof n === "string" && /^-?\d+$/.test(n));
    return '<div class="tile"><small>' + esc(label) + '</small><b class="' + (cls || "") +
      '"' + (num ? ' data-n="' + n + '"' : "") + '>' + n + "</b></div>";
  }
  function animateNums(root) {
    root.querySelectorAll("[data-n]").forEach(function (el) {
      var target = parseInt(el.dataset.n, 10) || 0;
      if (reduceMotion) { el.textContent = target; return; }
      var t0 = null;
      function step(t) {
        if (!t0) t0 = t;
        var k = Math.min(1, (t - t0) / 600);
        el.textContent = Math.round(target * k);
        if (k < 1) requestAnimationFrame(step);
      }
      requestAnimationFrame(step);
    });
  }
  function dayline(entries, getUnix, barsId, wrapId) {
    var now = Date.now() / 1000, buckets = [];
    for (var i = 13; i >= 0; i--) buckets.push({ t: now - i * 86400, n: 0 });
    entries.forEach(function (e) {
      var u = getUnix(e);
      if (!u) return;
      var d = Math.floor((now - u) / 86400);
      if (d >= 0 && d < 14) buckets[13 - d].n++;
    });
    var max = Math.max.apply(null, buckets.map(function (b) { return b.n; }).concat([1]));
    var recent = buckets[13].n + buckets[12].n;
    $(barsId).innerHTML = buckets.map(function (b) {
      var h = Math.max(3, Math.round((b.n / max) * 62));
      var dt = new Date(b.t * 1000);
      return '<div style="height:' + h + "px" + (b.n >= 5 ? '" class="hot' : "") +
        '" title="' + dt.toISOString().slice(0, 10) + " · " + b.n + '"></div>';
    }).join("");
    $(wrapId).classList.remove("hidden");
    return recent;
  }
  var lastResults = {};
  function exportJSON(mod) {
    var data = lastResults[mod];
    if (!data) { toast("NOTHING TO EXPORT — RUN FIRST"); return; }
    var blob = new Blob([JSON.stringify(data, null, 2)], { type: "application/json" });
    var a = document.createElement("a");
    a.href = URL.createObjectURL(blob);
    a.download = "maxim-" + mod + "-" + Date.now() + ".json";
    document.body.appendChild(a);
    a.click();
    setTimeout(function () { URL.revokeObjectURL(a.href); a.remove(); }, 500);
    toast("EXPORTED maxim-" + mod + ".json");
  }
  ["Services:services", "Tasks:scheduler", "Left:leftovers", "Prefetch:prefetch", "Amc:amcache", "Bam:bam", "Alts:alts", "Usn:usn"].forEach(function (pair) {
    var parts = pair.split(":"), btn = $("exp" + parts[0]), mod = parts[1];
    if (btn) btn.addEventListener("click", function () { exportJSON(mod); });
  });

  /* ---------- modal with tabs ---------- */
  var modalTabs = [];
  function openModal(title, sub, tabs) {
    modalTabs = tabs;
    $("mTitle").textContent = title;
    $("mSub").textContent = sub;
    var tb = $("mTabs");
    tb.innerHTML = "";
    tabs.forEach(function (t, i) {
      var b = document.createElement("button");
      b.textContent = t.label;
      if (i === 0) b.classList.add("active");
      b.addEventListener("click", function () {
        tb.querySelectorAll("button").forEach(function (x) { x.classList.remove("active"); });
        b.classList.add("active");
        $("mBody").innerHTML = t.html;
        Snd.tick();
      });
      tb.appendChild(b);
    });
    $("mBody").innerHTML = tabs.length ? tabs[0].html : "";
    $("modal").classList.remove("hidden");
    Snd.open();
  }
  function closeModal() { $("modal").classList.add("hidden"); Snd.shut(); }
  $("mClose").addEventListener("click", closeModal);
  $("modal").addEventListener("click", function (e) { if (e.target === $("modal")) closeModal(); });
  function kv(rows) {
    return '<dl class="kv">' + rows.map(function (r) {
      return "<dt>" + esc(r[0]) + "</dt><dd>" + r[1] + "</dd>";
    }).join("") + "</dl>";
  }

  /* ================= SERVICES ================= */
  $("runServices").addEventListener("click", function () {
    var btn = $("runServices");
    setBusy(btn, true); Snd.arp();
    $("servicesOut").innerHTML = "";
    api("service_checker_run").then(function (r) {
      lastResults.services = r;
      renderServices(r);
      Snd.ok();
    }).catch(function (e) { Snd.err(); toast(String((e && e.message) || e)); })
      .finally(function () { setBusy(btn, false); });
  });
  function sevSq(s) {
    return '<span class="sq ' + (s === "bad" ? "bad" : s === "warn" ? "warn" : "ok") + '"></span>';
  }
  function pill(sev, text) {
    return '<span class="sig ' + esc(sev || "") + '">' + esc(text || sev || "") + "</span>";
  }
  function sessionHits(r) {
    if (!r.events || r.events.skipped) return 0;
    return [r.events.usnDeleted, r.events.logClosed, r.events.logFull, r.events.serviceInstalled]
      .filter(function (s) { return s && s.detected; }).length;
  }
  function sigRow(s) {
    if (!s) return "";
    var state = !s.detected ? "<span class='meta'>NO DATA</span>"
      : s.severity === "bad" ? pill("bad", "HIT " + (s.when || ""))
      : s.severity === "warn" ? pill("warn", "SEEN " + (s.when || ""))
      : pill("ok", s.when || "OK");
    return "<tr><td>" + sevSq(!s.detected ? "ok" : s.severity) + "</td><td>" + esc(s.key) +
      "</td><td class='mono'>" + state + "</td></tr>";
  }
  function renderServices(r) {
    if (!r) return;
    setAdmin(true, !!r.admin);
    var n = r.findingCount || 0;
    verdict("svcVerdict", n === 0 ? "ok" : n < 4 ? "warn" : "bad",
      n === 0 ? "■ VERDICT: CLEAN" : "■ VERDICT: " + n + " FINDING" + (n > 1 ? "S" : ""),
      "BOOT " + ((r.boot && r.boot.lastBoot) || "?") + " · LOG " + ((r.events && r.events.eventLogStartAt) || "?") +
      (r.admin ? "" : " · LIMITED (NOT ADMIN)"));
    $("svcTiles").innerHTML =
      tile("FINDINGS", n, n === 0 ? "lime" : n < 4 ? "amber" : "red") +
      tile("SERVICES", (r.services || []).length, "") +
      tile("DRIVES", (r.drives || []).length, "") +
      tile("SESSION HITS", sessionHits(r), sessionHits(r) === 0 ? "lime" : "amber") +
      tile("BOOT", ((r.boot && r.boot.lastBoot) || "?").slice(0, 10), "");
    animateNums($("svcTiles"));
    var h = "";
    if (r.boot && r.boot.tickMismatch) {
      h += '<div class="verdict bad"><span>■</span><span>CLOCK/TICK MISMATCH <small>' +
        esc(r.boot.tickBootEstimate || "") + " " + esc(r.boot.tickNote || "") + "</small></span></div>";
    }
    h += '<div class="card"><h3>DRIVES</h3><div class="tblwrap"><table><thead><tr><th>DRIVE</th><th>FS</th><th>TYPE</th><th>SIZE</th></tr></thead><tbody>' +
      (r.drives || []).map(function (d) {
        return "<tr><td class='mono'>" + esc(d.letter) + "</td><td>" + esc(d.fileSystem) + "</td><td>" +
          esc(d.media) + "</td><td class='mono'>" + esc(d.size) + "</td></tr>";
      }).join("") + "</tbody></table></div></div>";
    h += '<div class="card"><h3>SERVICES</h3><div class="tblwrap"><table><thead><tr><th>ST</th><th>NAME</th><th>STATUS</th><th>START</th><th>PID</th></tr></thead><tbody>' +
      (r.services || []).map(function (s) {
        return "<tr><td>" + sevSq(s.severity) + "</td><td>" + esc(s.display || s.name) + "</td><td class='mono'>" +
          esc(s.status) + "</td><td class='mono'>" + esc(s.startType || "") + "</td><td class='mono'>" + esc(s.pid || "") + "</td></tr>";
      }).join("") + "</tbody></table></div></div>";
    if (r.events && !r.events.skipped) {
      h += '<div class="card"><h3>EVENTS + USN</h3><div class="mono" style="margin-bottom:8px">SHUTDOWN ' +
        esc(r.events.lastShutdown || "—") + " · UNEXPECTED " + esc(r.events.unexpectedShutdown ? r.events.unexpectedShutdownAt : "NO") +
        " · CLOCK " + esc(r.events.clockChanged ? r.events.clockChangedAt : "NO") + " · LOG " +
        esc(r.events.eventLogStartAt || "—") + " " + esc(r.events.eventLogStartNote || "") + "</div>" +
        '<div class="tblwrap"><table><thead><tr><th>ST</th><th>SIGNAL</th><th>STATE</th></tr></thead><tbody>' +
        (r.events.clears || []).map(function (c) {
          return "<tr><td>" + sevSq(c.detected ? "bad" : "ok") + "</td><td>" + esc(c.key) + "</td><td class='mono'>" +
            (c.detected ? "CLEARED " + esc(c.when || "") : "OK") + "</td></tr>";
        }).join("") +
        (r.events.usn || []).map(function (u) {
          return "<tr><td>" + sevSq(u.severity) + "</td><td>USN " + esc(u.volume) + "</td><td class='mono'>" + esc(u.state) + "</td></tr>";
        }).join("") + "</tbody></table></div></div>";
      h += '<div class="card"><h3>SESSION + DEVICE SIGNALS</h3><div class="tblwrap"><table><thead><tr><th>ST</th><th>SIGNAL</th><th>STATE</th></tr></thead><tbody>' +
        [r.events.usnDeleted, r.events.logClosed, r.events.logFull, r.events.serviceInstalled,
         r.events.procCreated, r.events.usbIn, r.events.usbOut].map(sigRow).join("") +
        "</tbody></table></div></div>";
    }
    if (r.events && !r.events.skipped && (r.events.usbDevices || []).length) {
      h += '<div class="card"><h3>USB DEVICES (' + r.events.usbDevices.length + ')</h3><div class="tblwrap"><table><thead><tr><th>DEVICE</th><th>SERIAL</th></tr></thead><tbody>' +
        r.events.usbDevices.map(function (u) {
          return "<tr><td>" + esc(u.name) + "</td><td class='mono'>" + esc(u.serial) + "</td></tr>";
        }).join("") + "</tbody></table></div></div>";
    }
    if (r.events && !r.events.skipped && (r.events.avProducts || []).length) {
      h += '<div class="card"><h3>ANTIVIRUS (' + r.events.avProducts.length + ')</h3><div class="tblwrap"><table><thead><tr><th>PRODUCT</th><th>STATE</th><th>SOURCE</th></tr></thead><tbody>' +
        r.events.avProducts.map(function (a) {
          return "<tr><td>" + esc(a.name) + "</td><td class='mono'>" + esc(a.state) + "</td><td class='mono'>" + esc(a.source) + "</td></tr>";
        }).join("") + "</tbody></table></div></div>";
    }
    if (r.recycle && !r.recycle.skipped) {
      h += '<div class="card"><h3>RECYCLE BIN</h3><div class="mono">' + esc(r.recycle.items) + " ITEMS · " +
        esc(r.recycle.totalSizeLabel) + " · THIS SESSION " + esc(r.recycle.deletedThisSession) +
        " · NEWEST " + esc(r.recycle.newestAt || "—") + " " + esc(r.recycle.newestPath || "") + "</div></div>";
    }
    $("servicesOut").innerHTML = h || '<div class="empty">NO DATA</div>';
  }

  /* ================= SCHEDULED TASKS ================= */
  var taskEntries = [];
  $("runTasks").addEventListener("click", function () {
    var btn = $("runTasks");
    setBusy(btn, true); Snd.arp();
    $("tasksMeta").textContent = "READING TASK LIBRARY…";
    api("scheduler_run").then(function (r) {
      if (typeof r.admin === "boolean") setAdmin(true, r.admin);
      if (r.error) { Snd.err(); toast(r.error); $("tasksMeta").textContent = r.error; return; }
      lastResults.scheduler = r;
      taskEntries = r.entries || [];
      var bad = taskEntries.filter(function (e) { return e.severity === "bad"; }).length;
      $("tasksMeta").textContent = taskEntries.length + " TASKS · " + bad + " BAD" +
        (r.skipped ? " · " + r.skipped + " SKIPPED" : "");
      drawTasks();
      Snd.ok();
    }).catch(function (e) { Snd.err(); toast(String((e && e.message) || e)); })
      .finally(function () { setBusy(btn, false); });
  });
  $("taskFilter").addEventListener("input", drawTasks);
  $("taskSort").addEventListener("change", drawTasks);
  document.querySelectorAll("#taskChips input").forEach(function (c) {
    c.addEventListener("change", drawTasks);
  });
  function drawTasks() {
    var st = chipState("taskChips"), sort = $("taskSort").value;
    var q = ($("taskFilter").value || "").toLowerCase();
    var rank = { bad: 0, warn: 1, ok: 2 };
    var list = taskEntries.filter(function (e) {
      if (q && ((e.name || "") + " " + (e.action || "") + " " + (e.author || "")).toLowerCase().indexOf(q) < 0) return false;
      if (!st[e.severity]) return false;
      if (st.boot && (e.triggers || []).every(function (t) { return t !== "BOOT" && t !== "LOGON"; })) return false;
      return true;
    });
    list.sort(function (a, b) {
      if (sort === "name") return String(a.name || "").localeCompare(String(b.name || ""));
      if (sort === "new") return (b.createdUnix || 0) - (a.createdUnix || 0);
      var d = (rank[a.severity] - rank[b.severity]) || ((b.createdUnix || 0) - (a.createdUnix || 0));
      return d;
    });
    if (!list.length) { $("tasksOut").innerHTML = '<div class="empty">NO TASKS MATCH.</div>'; return; }
    $("tasksOut").innerHTML = "<table><thead><tr><th>ST</th><th>TASK</th><th>TRIGGERS</th><th>ACTION</th><th>CREATED</th></tr></thead><tbody>" +
      list.slice(0, 500).map(function (e) {
        var bad = e.severity === "bad";
        return '<tr class="' + (bad ? "row-bad" : e.severity === "warn" ? "row-warn" : "") + '"><td>' +
          sevSq(e.severity) + "</td><td class='mono'>" + esc(e.name) +
          "<br><span class='meta'>" + esc(e.folder || "") + (e.enabled ? "" : " · DISABLED") + "</span>" +
          ((e.reasons || []).length ? "<br>" + e.reasons.map(function (x) {
            return '<span class="rule">' + esc(x) + "</span>";
          }).join("") : "") +
          "</td><td class='mono'>" + esc((e.triggers || []).join(" · ")) +
          "</td><td class='mono'>" + esc(e.action || "—") +
          (e.args ? "<br><span class='meta'>" + esc(e.args.slice(0, 120)) + "</span>" : "") +
          (e.missing ? ' <span class="sig bad">MISSING</span>' : "") +
          "</td><td class='mono'>" + esc(e.created || "—") + "</td></tr>";
      }).join("") + "</tbody></table>" +
      (list.length > 500 ? '<div class="meta" style="padding:8px 10px">SHOWING 500 OF ' + list.length + "</div>" : "");
  }

  /* ================= LEFTOVERS ================= */
  var leftData = null;
  $("runLeft").addEventListener("click", function () {
    var btn = $("runLeft");
    setBusy(btn, true); Snd.arp();
    $("leftMeta").textContent = "SWEEPING TEMP · RECENT · DUMPS · HOSTS…";
    api("leftover_run").then(function (r) {
      if (typeof r.admin === "boolean") setAdmin(true, r.admin);
      if (r.error) { Snd.err(); toast(r.error); $("leftMeta").textContent = r.error; return; }
      lastResults.leftovers = r;
      leftData = r;
      var n = r.findingCount || 0;
      $("leftMeta").textContent =
        (r.tempFiles || []).length + " TEMP · " + (r.recentLinks || []).length + " RECENT · " +
        (r.dumps || []).length + " DUMPS · " + (r.hosts || []).length + " HOSTS · " +
        (r.history || []).length + " HITS · " + n + " BAD" +
        (r.hostsModified ? " · HOSTS MODIFIED THIS SESSION" : "");
      drawLeft();
      Snd.ok();
    }).catch(function (e) { Snd.err(); toast(String((e && e.message) || e)); })
      .finally(function () { setBusy(btn, false); });
  });
  $("leftFilter").addEventListener("input", drawLeft);
  $("leftSec").addEventListener("change", drawLeft);
  document.querySelectorAll("#leftChips input").forEach(function (c) {
    c.addEventListener("change", drawLeft);
  });
  function leftPass(e, q, st) {
    if (!st[e.severity]) return false;
    if (!q) return true;
    return ((e.name || "") + " " + (e.path || "") + " " + (e.note || "") +
      " " + (e.host || "") + " " + (e.preview || "")).toLowerCase().indexOf(q) >= 0;
  }
  function leftRowFile(f) {
    var cls = f.severity === "bad" ? "row-bad" : f.severity === "warn" ? "row-warn" : "";
    return '<tr class="' + cls + '"><td>' + sevSq(f.severity) + "</td><td class='mono'>" + esc(f.name) +
      "<br><span class='meta'>" + esc(f.path || "") + "</span>" +
      (f.note ? "<br>" + '<span class="rule">' + esc(f.note) + "</span>" : "") +
      "</td><td class='mono'>" + esc(f.sizeLabel || "") + "</td><td class='mono'>" + esc(f.modified || "") + "</td></tr>";
  }
  function drawLeft() {
    if (!leftData) return;
    var st = chipState("leftChips"), sec = $("leftSec").value;
    var q = ($("leftFilter").value || "").toLowerCase();
    var h = "";
    function block(title, rows) {
      if (!rows.length) return;
      h += '<div class="card"><h3>' + title + " (" + rows.length + ")</h3>" +
        '<div class="tblwrap"><table><thead><tr><th>ST</th><th>ITEM</th><th>SIZE</th><th>MODIFIED</th></tr></thead><tbody>' +
        rows.map(leftRowFile).join("") + "</tbody></table></div></div>";
    }
    if (sec === "all" || sec === "temp")
      block("TEMP FILES", (leftData.tempFiles || []).filter(function (e) { return leftPass(e, q, st); }));
    if (sec === "all" || sec === "recent")
      block("RECENT LINKS", (leftData.recentLinks || []).filter(function (e) { return leftPass(e, q, st); }));
    if (sec === "all" || sec === "dumps")
      block("CRASH DUMPS", (leftData.dumps || []).filter(function (e) { return leftPass(e, q, st); }));
    if ((sec === "all" || sec === "hosts") && (leftData.hosts || []).length) {
      var hosts = leftData.hosts.filter(function (x) {
        return st[x.severity] && (!q || (x.ip + " " + x.host).toLowerCase().indexOf(q) >= 0);
      });
      h += '<div class="card"><h3>HOSTS FILE (' + hosts.length +
        (leftData.hostsModified ? ' · <span class="pill warn">MODIFIED THIS SESSION</span>' : "") +
        ")</h3>" + '<div class="tblwrap"><table><thead><tr><th>ST</th><th>IP</th><th>HOST</th></tr></thead><tbody>' +
        hosts.map(function (x) {
          return "<tr><td>" + sevSq(x.severity) + "</td><td class='mono'>" + esc(x.ip) +
            "</td><td class='mono'>" + esc(x.host) + "</td></tr>";
        }).join("") + "</tbody></table></div></div>";
    }
    if ((sec === "all" || sec === "hist") && (leftData.history || []).length) {
      var hits = leftData.history.filter(function (x) {
        return st[x.severity] && (!q || (x.preview || "").toLowerCase().indexOf(q) >= 0);
      });
      h += '<div class="card"><h3>PS HISTORY HITS (' + hits.length + ")</h3>" +
        '<div class="list mono">' + hits.map(function (x) {
          return "<div>" + sevSq(x.severity) + esc(x.preview) + "</div>";
        }).join("") + "</div></div>";
    }
    $("leftOut").innerHTML = h || '<div class="empty">NO LEFTOVERS MATCH.</div>';
  }

  /* ================= PREFETCH ================= */
  var prefetchEntries = [];
  function pfFilter(e) {
    var q = ($("prefetchFilter").value || "").toLowerCase();
    return !q || (e.filename + " " + (e.properPath || "") + " " + (e.matchedRules || []).join(" ")).toLowerCase().indexOf(q) >= 0;
  }
  function chipState(id) {
    var m = {};
    document.querySelectorAll("#" + id + " input").forEach(function (c) { m[c.dataset.k] = c.checked; });
    return m;
  }
  document.querySelectorAll("#pfChips input").forEach(function (c) {
    c.addEventListener("change", drawPrefetch);
  });
  $("runPrefetch").addEventListener("click", function () {
    var btn = $("runPrefetch");
    setBusy(btn, true); Snd.arp();
    api("prefetch_run").then(function (r) {
      if (typeof r.admin === "boolean") setAdmin(true, r.admin);
      lastResults.prefetch = r;
      prefetchEntries = r.entries || [];
      renderPrefetchMeta(r);
      drawPrefetch();
      Snd.ok();
    }).catch(function (e) { Snd.err(); toast(String((e && e.message) || e)); })
      .finally(function () { setBusy(btn, false); });
  });
  $("prefetchFilter").addEventListener("input", drawPrefetch);
  $("pfSort").addEventListener("change", drawPrefetch);
  function pfHits(e) { return (e.matchedRules || []).some(function (x) { return x !== "none"; }); }
  function renderPrefetchMeta(r) {
    var n = r.findingCount || 0;
    verdict("pfVerdict", n === 0 ? "ok" : n < 10 ? "warn" : "bad",
      n === 0 ? "■ VERDICT: CLEAN" : "■ VERDICT: " + n + " FINDINGS",
      (r.filesFound || 0) + " FILES" + (r.admin ? "" : " · NOT ADMIN"));
    $("pfTiles").innerHTML =
      tile("ENTRIES", prefetchEntries.length, "") +
      tile("SUSPICIOUS", n, n === 0 ? "lime" : n < 10 ? "amber" : "red") +
      tile("FILES", r.filesFound || 0, "") +
      tile("DIR", (r.prefetchDir || "").split("\\").pop() || "PREFETCH", "");
    animateNums($("pfTiles"));
  }
  function drawPrefetch() {
    var st = chipState("pfChips"), sort = $("pfSort").value;
    var list = prefetchEntries.filter(pfFilter).filter(function (e) {
      var hits = pfHits(e);
      var ok = (st.unsigned && !e.isSigned) || (st.hits && hits) ||
        (st.clean && e.isSigned && !hits) || (st.missing && !e.isPresent);
      if (!ok) return false;
      if (st.session && !e.isInInstance) return false;
      return true;
    });
    list.sort(function (a, b) {
      if (sort === "name") return String(a.filename || "").localeCompare(String(b.filename || ""));
      var d = (b.executedUnix || 0) - (a.executedUnix || 0);
      return sort === "old" ? -d : d;
    });
    dayline(prefetchEntries, function (e) { return e.executedUnix; }, "pfBars", "pfTime");
    if (!list.length) { $("prefetchOut").innerHTML = '<div class="empty">NO ENTRIES MATCH.</div>'; return; }
    $("prefetchOut").innerHTML = "<table><thead><tr><th>ST</th><th>FILE</th><th>LAST RUN</th><th>RUNS</th><th>SIG</th><th>RULES</th></tr></thead><tbody>" +
      list.slice(0, 500).map(function (e) {
        var i = prefetchEntries.indexOf(e);
        var sus = !e.isSigned || pfHits(e);
        return '<tr data-i="' + i + '"><td>' + sevSq(sus ? "bad" : "ok") + "</td><td class='mono'>" + esc(e.filename) +
          "</td><td class='mono'>" + esc(e.readableTime || "") + (e.isInInstance ? ' <span class="rule">SESSION</span>' : "") +
          "</td><td class='mono'>" + esc(e.runCount) + "</td><td>" +
          (e.isSigned ? '<span class="sig ok">SIGNED</span>' : '<span class="sig bad">UNSIGNED</span>') +
          "</td><td>" + esc((e.matchedRules || []).join(", ")) + "</td></tr>";
      }).join("") + "</tbody></table>" +
      (list.length > 500 ? '<div class="meta" style="padding:8px 10px">SHOWING 500 OF ' + list.length + "</div>" : "");
    $("prefetchOut").querySelectorAll("tr[data-i]").forEach(function (tr) {
      tr.addEventListener("click", function () { openPrefetch(prefetchEntries[+tr.dataset.i]); });
    });
  }
  function openPrefetch(e) {
    if (!e) return;
    var tabs = [
      { label: "SUMMARY", html: kv([
        ["FILE", "<span class='mono'>" + esc(e.filename) + "</span>"],
        ["PATH", "<span class='mono'>" + esc(e.properPath || "—") + "</span>"],
        ["LAST RUN", "<span class='mono'>" + esc(e.readableTime || "—") + "</span>"],
        ["RUNS", "<span class='mono'>" + esc(e.runCount) + "</span>"],
        ["HASH", "<span class='mono'>" + esc(e.prefetchHash || "") + "</span>"],
        ["SIGNED", e.isSigned ? "YES" : "NO"],
        ["PRESENT", e.isPresent ? "YES" : "NO"],
        ["IN SESSION", e.isInInstance ? "YES" : "NO"]
      ])},
      { label: "RULES (" + (e.matchedDetails || []).length + ")", html:
        (e.matchedDetails || []).map(function (d) {
          return kv([["ID", "<span class='mono'>" + esc(d.id) + " · " + esc(d.name) + "</span>"],
            ["SAMPLE", "<span class='mono'>" + esc(d.sample) + "</span>"],
            ["OFFSET", "<span class='mono'>" + esc(d.offset) + "</span>"]]);
        }).join("") || '<div class="empty">NO RULE HITS</div>' },
      { label: "RELATED (" + (e.relatedTotal || 0) + ")", html:
        '<div class="list mono">' + (e.relatedFiles || []).map(function (f) {
          return "<div>" + (f.present ? "■ " : "□ ") + esc(f.path) + "</div>";
        }).join("") + "</div>" }
    ];
    openModal(e.filename, (e.properPath || "") + " · " + (e.readableTime || ""), tabs);
  }
  $("checkRelated").addEventListener("click", function () {
    var paths = [];
    prefetchEntries.forEach(function (e) { (e.relatedFiles || []).forEach(function (f) { paths.push(f.path); }); });
    paths = paths.slice(0, 200);
    if (!paths.length) { toast("RUN PREFETCH SCAN FIRST"); return; }
    api("prefetch_related_signatures", { paths: paths }).then(function (res) {
      var bad = (res || []).filter(function (x) { return !x.signed; });
      toast("RELATED: " + res.length + " CHECKED · " + bad.length + " UNSIGNED");
    }).catch(function (e) { toast(String((e && e.message) || e)); });
  });

  /* ================= AMCACHE ================= */
  var amcEntries = [];
  $("runAmc").addEventListener("click", function () {
    var btn = $("runAmc");
    setBusy(btn, true); Snd.arp();
    $("amcMeta").textContent = "MOUNTING AMCACHE.HVE…";
    api("amcache_run").then(function (r) {
      if (typeof r.admin === "boolean") setAdmin(true, r.admin);
      if (r.error) { Snd.err(); toast(r.error); $("amcMeta").textContent = r.error; return; }
      lastResults.amcache = r;
      amcEntries = r.entries || [];
      var bad = amcEntries.filter(function (e) { return e.severity === "bad"; }).length;
      $("amcMeta").textContent = (r.keysScanned || 0) + " KEYS · " + amcEntries.length +
        " FILES · " + bad + " BAD";
      drawAmc();
      Snd.ok();
    }).catch(function (e) { Snd.err(); toast(String((e && e.message) || e)); })
      .finally(function () { setBusy(btn, false); });
  });
  $("amcFilter").addEventListener("input", drawAmc);
  $("amcSort").addEventListener("change", drawAmc);
  document.querySelectorAll("#amcChips input").forEach(function (c) {
    c.addEventListener("change", drawAmc);
  });
  function drawAmc() {
    var st = chipState("amcChips"), sort = $("amcSort").value;
    var q = ($("amcFilter").value || "").toLowerCase();
    var rank = { bad: 0, warn: 1, ok: 2 };
    var list = amcEntries.filter(function (e) {
      if (q && ((e.file || "") + " " + (e.path || "") + " " + (e.details || "")).toLowerCase().indexOf(q) < 0) return false;
      return !!st[e.severity];
    });
    list.sort(function (a, b) {
      if (sort === "name") return String(a.file || "").localeCompare(String(b.file || ""));
      if (sort === "new") return (b.keyUnix || 0) - (a.keyUnix || 0);
      return (rank[a.severity] - rank[b.severity]) || ((b.keyUnix || 0) - (a.keyUnix || 0));
    });
    if (!list.length) { $("amcOut").innerHTML = '<div class="empty">NO ENTRIES MATCH.</div>'; return; }
    $("amcOut").innerHTML = "<table><thead><tr><th>ST</th><th>FILE</th><th>KEY TIME</th></tr></thead><tbody>" +
      list.slice(0, 500).map(function (e) {
        return '<tr><td>' + sevSq(e.severity) + "</td><td class='mono'>" + esc(e.file) +
          "<br><span class='meta'>" + esc(e.path || "") + "</span>" +
          (e.missing ? ' <span class="sig bad">MISSING</span>' : "") +
          ((e.reasons || []).length ? "<br>" + e.reasons.map(function (x) {
            return '<span class="rule">' + esc(x) + "</span>";
          }).join("") : "") +
          (e.details ? "<br><span class='meta'>" + esc(e.details.slice(0, 220)) + "</span>" : "") +
          "</td><td class='mono'>" + esc(e.keyTime || "—") + "</td></tr>";
      }).join("") + "</tbody></table>" +
      (list.length > 500 ? '<div class="meta" style="padding:8px 10px">SHOWING 500 OF ' + list.length + "</div>" : "");
  }

  /* ================= BAM ================= */
  var bamEntries = [];
  $("runBam").addEventListener("click", function () {
    var btn = $("runBam");
    setBusy(btn, true); Snd.arp();
    api("bam_run").then(function (r) {
      if (typeof r.admin === "boolean") setAdmin(true, r.admin);
      if (r.error) { Snd.err(); toast(r.error); return; }
      lastResults.bam = r;
      bamEntries = r.entries || [];
      renderBamMeta(r);
      drawBam();
      Snd.ok();
    }).catch(function (e) { Snd.err(); toast(String((e && e.message) || e)); })
      .finally(function () { setBusy(btn, false); });
  });
  document.querySelectorAll("#bamChips input").forEach(function (c) {
    c.addEventListener("change", drawBam);
  });
  $("bamFilter").addEventListener("input", drawBam);
  $("bamSort").addEventListener("change", drawBam);
  function bamHasHit(e) {
    return (e.matchedRules || []).some(function (x) { return x !== "none"; }) ||
      (e.replaceResults || []).length > 0;
  }
  function renderBamMeta(r) {
    var n = r.findingCount || 0;
    verdict("bamVerdict", n === 0 ? "ok" : n < 10 ? "warn" : "bad",
      n === 0 ? "■ VERDICT: CLEAN" : "■ VERDICT: " + n + " FINDINGS",
      bamEntries.length + " ENTRIES · DRIVES " + (r.drivesScanned || 0));
    $("bamTiles").innerHTML =
      tile("ENTRIES", bamEntries.length, "") +
      tile("SUSPICIOUS", n, n === 0 ? "lime" : n < 10 ? "amber" : "red") +
      tile("DRIVES", r.drivesScanned || 0, "") +
      tile("SKIPPED", r.drivesSkipped || 0, "");
    animateNums($("bamTiles"));
  }
  function drawBam() {
    var st = chipState("bamChips"), sort = $("bamSort").value;
    var q = ($("bamFilter").value || "").toLowerCase();
    var list = bamEntries.filter(function (e) {
      if (q && (e.path || "").toLowerCase().indexOf(q) < 0) return false;
      var ok = (st.signed && e.signature === "Signed") ||
        (st.unsigned && e.signature === "Not signed") ||
        (st.deleted && e.signature === "Deleted") ||
        (st.hits && bamHasHit(e)) ||
        (st.replace && (e.replaceResults || []).length > 0);
      if (!ok) return false;
      if (st.session && !e.isInInstance) return false;
      return true;
    });
    list.sort(function (a, b) {
      if (sort === "path") return String(a.path || "").localeCompare(String(b.path || ""));
      var d = (b.executedUnix || 0) - (a.executedUnix || 0);
      return sort === "old" ? -d : d;
    });
    dayline(bamEntries, function (e) { return e.executedUnix; }, "bamBars", "bamTime");
    if (!list.length) { $("bamOut").innerHTML = '<div class="empty">NO ENTRIES MATCH.</div>'; return; }
    $("bamOut").innerHTML = "<table><thead><tr><th>ST</th><th>PATH</th><th>LAST EXEC</th><th>SIG</th><th>RULES</th></tr></thead><tbody>" +
      list.slice(0, 500).map(function (e) {
        var i = bamEntries.indexOf(e);
        var bad = bamHasHit(e) || e.signature !== "Signed";
        var sig = e.signature === "Signed" ? '<span class="sig ok">SIGNED</span>' :
          e.signature === "Deleted" ? '<span class="sig warn">DELETED</span>' : '<span class="sig bad">UNSIGNED</span>';
        return '<tr data-i="' + i + '"><td>' + sevSq(bad ? "bad" : "ok") + "</td><td class='mono'>" + esc(e.path) +
          "</td><td class='mono'>" + esc(e.readableTime || "") + (e.isInInstance ? ' <span class="rule">SESSION</span>' : "") +
          "</td><td>" + sig + "</td><td>" + esc((e.matchedRules || []).join(", ")) + "</td></tr>";
      }).join("") + "</tbody></table>" +
      (list.length > 500 ? '<div class="meta" style="padding:8px 10px">SHOWING 500 OF ' + list.length + "</div>" : "");
    $("bamOut").querySelectorAll("tr[data-i]").forEach(function (tr) {
      tr.addEventListener("click", function () { openBam(bamEntries[+tr.dataset.i]); });
    });
  }
  function openBam(e) {
    if (!e) return;
    var tabs = [
      { label: "SUMMARY", html: kv([
        ["PATH", "<span class='mono'>" + esc(e.path) + "</span>"],
        ["LAST EXEC", "<span class='mono'>" + esc(e.readableTime || "—") + "</span>"],
        ["SIGNATURE", esc(e.signature || "")],
        ["IN SESSION", e.isInInstance ? "YES" : "NO"]
      ])},
      { label: "RULES (" + (e.matchedDetails || []).length + ")", html:
        (e.matchedDetails || []).map(function (d) {
          return kv([["ID", "<span class='mono'>" + esc(d.id) + " · " + esc(d.name) + "</span>"],
            ["SAMPLE", "<span class='mono'>" + esc(d.sample) + "</span>"]]);
        }).join("") || '<div class="empty">NO RULE HITS</div>' },
      { label: "TAMPER (" + (e.replaceResults || []).length + ")", html:
        (e.replaceResults || []).map(function (x) {
          return kv([["FILE", "<span class='mono'>" + esc(x.filename) + "</span>"],
            ["TYPE", esc(x.replaceType)], ["DETAIL", esc(x.details)]]);
        }).join("") || '<div class="empty">NO TAMPER FLAGS</div>' }
    ];
    openModal(e.path.split("\\").pop() || e.path, e.path, tabs);
  }

  /* ================= ALTS ================= */
  $("runAlts").addEventListener("click", function () {
    var btn = $("runAlts");
    setBusy(btn, true); Snd.arp();
    api("alt_detector_run").then(function (r) {
      lastResults.alts = r;
      var mc = r.minecraftAccounts || [], dc = r.discordIds || [];
      verdict("altsVerdict", "ok", "■ SWEEP COMPLETE", mc.length + " MC · " + dc.length + " DISCORD");
      $("altsTiles").innerHTML =
        tile("MC ACCOUNTS", mc.length, mc.length > 1 ? "amber" : "lime") +
        tile("DISCORD IDS", dc.length, dc.length > 1 ? "amber" : "lime") +
        tile("LAUNCHER FILES", r.launcherFilesScanned || 0, "") +
        tile("LOG FILES", r.logFilesScanned || 0, "");
      animateNums($("altsTiles"));
      $("mcList").innerHTML = mc.map(function (u) { return "<div>" + esc(u) + "</div>"; }).join("") || "—";
      var profs = r.profiles || [];
      $("altsProfList").innerHTML = profs.map(function (p) {
        return "<div>" + esc(p.path) + " <span class='meta'>" + esc(p.kind) +
          (p.lastUsed ? " · USED " + esc(p.lastUsed) : " · NEVER USED") +
          (p.exists ? "" : " · MISSING") + "</span></div>";
      }).join("") || "—";
      var accts = {};
      (r.discordAccounts || []).forEach(function (a) { accts[a.id] = a.username; });
      $("dcList").innerHTML = dc.map(function (id) {
        return "<div>" + esc(id) + (accts[id] ? " — " + esc(accts[id]) : "") + "</div>";
      }).join("") || "—";
      var hw = r.hwid || {};
      $("hwidBox").innerHTML = "GUID " + esc(hw.machineGuid || "—") +
        ((hw.disks || []).map(function (d) { return "<br>DISK " + esc(d); }).join("") || "");
      $("modsList").innerHTML = ((r.mods || []).map(function (m) {
        return "<div>" + esc(m.name) + " <span class='meta'>" + esc(m.sizeLabel || "") + "</span></div>";
      }).join("") || "—");
      Snd.ok();
    }).catch(function (e) { Snd.err(); toast(String((e && e.message) || e)); })
      .finally(function () { setBusy(btn, false); });
  });
  $("clearAlts").addEventListener("click", function () {
    api("alt_detector_clear").then(function () { toast("ALT CACHE CLEARED"); }).catch(toast);
  });

  /* ================= USN EXPLORER ================= */
  var usnEntries = [];
  api("usn_drives").then(function (drives) {
    var sel = $("usnDrive");
    sel.innerHTML = "";
    (drives || []).forEach(function (d, i) {
      var o = document.createElement("option");
      o.value = d;
      o.textContent = d;
      sel.appendChild(o);
    });
    if (!sel.options.length) {
      var o = document.createElement("option");
      o.value = "C:";
      o.textContent = "C:";
      sel.appendChild(o);
    }
  }).catch(function () {});
  $("runUsn").addEventListener("click", function () {
    var btn = $("runUsn");
    var drive = $("usnDrive").value || "C:";
    setBusy(btn, true); Snd.arp();
    $("usnMeta").textContent = "WALKING " + drive + " JOURNAL…";
    api("usn_run", { paths: [drive] }).then(function (r) {
      if (typeof r.admin === "boolean") setAdmin(true, r.admin);
      if (r.error) { Snd.err(); toast(r.error); $("usnMeta").textContent = r.error; return; }
      lastResults.usn = r;
      usnEntries = r.entries || [];
      usnBoot = r.bootUnix || 0;
      var n = r.findingCount || 0;
      verdict("usnVerdict", n === 0 ? "ok" : "warn", "■ WALK COMPLETE",
        (r.scanned || 0) + " RECORDS · " + (r.batches || 0) + " BATCHES · " +
        (r.dirsMapped || 0) + " DIRS · " + (r.skips || 0) + " SKIPS · " +
        (r.matched || 0) + " EXT-MATCHED · " + usnEntries.length + " SHOWN · " + n + " THIS SESSION" +
        (r.truncated ? " · PARTIAL TAIL" : ""));
      $("usnTiles").innerHTML =
        tile("SCANNED", r.scanned || 0, "") +
        tile("MATCHES", usnEntries.length, "") +
        tile("SESSION", n, n === 0 ? "lime" : "amber") +
        tile("DRIVE", (r.drive || drive).replace(":", ""), "");
      animateNums($("usnTiles"));
      drawUsn();
      $("usnMeta").textContent = usnEntries.length + " MATCHES SHOWN · " +
        (r.resolved || 0) + " PATHS · " + (r.dated || 0) + " DATED · BLD " + (r.built || "?");
      Snd.ok();
    }).catch(function (e) { Snd.err(); toast(String((e && e.message) || e)); })
      .finally(function () { setBusy(btn, false); });
  });
  $("usnFilter").addEventListener("input", drawUsn);
  $("usnSort").addEventListener("change", drawUsn);
  document.querySelectorAll("#usnChips input").forEach(function (c) {
    c.addEventListener("change", drawUsn);
  });
  var usnBoot = 0;
  function drawUsn() {
    var st = chipState("usnChips"), sort = $("usnSort").value;
    var q = ($("usnFilter").value || "").toLowerCase();
    var list = usnEntries.filter(function (e) {
      if (q && ((e.name || "") + " " + (e.path || "")).toLowerCase().indexOf(q) < 0) return false;
      if (!st[e.ext]) return false;
      if (st.session && usnBoot && (e.unix || 0) < usnBoot) return false;
      return true;
    });
    list.sort(function (a, b) {
      if (sort === "name") return String(a.name || "").localeCompare(String(b.name || ""));
      var d = (b.unix || 0) - (a.unix || 0);
      return sort === "old" ? -d : d;
    });
    if (!list.length) { $("usnOut").innerHTML = '<div class="empty">NO ENTRIES MATCH.</div>'; return; }
    $("usnOut").innerHTML = "<table><thead><tr><th>TIME</th><th>NAME</th><th>EXT</th><th>REASON</th></tr></thead><tbody>" +
      list.slice(0, 500).map(function (e) {
        var i = usnEntries.indexOf(e);
        return '<tr data-i="' + i + '"><td class="mono">' + esc(e.time || "") +
          "</td><td class='mono'>" + esc(e.name) + "<br><span class='meta'>" + esc(e.path || "") + "</span>" +
          (e.gone ? ' <span class="sig bad">GONE</span>' : "") +
          "</td><td class='mono'>" + esc(e.ext) + "</td><td>" + esc(e.reason || "") + "</td></tr>";
      }).join("") + "</tbody></table>" +
      (list.length > 500 ? '<div class="meta" style="padding:8px 10px">SHOWING 500 OF ' + list.length + "</div>" : "");
    $("usnOut").querySelectorAll("tr[data-i]").forEach(function (tr) {
      tr.addEventListener("click", function () { openUsn(usnEntries[+tr.dataset.i]); });
    });
  }
  function openUsn(e) {
    if (!e) return;
    openModal(e.name, e.path || "", [
      { label: "SUMMARY", html: kv([
        ["NAME", "<span class='mono'>" + esc(e.name) + "</span>"],
        ["PATH", "<span class='mono'>" + esc(e.path || "—") + "</span>"],
        ["TIME", "<span class='mono'>" + esc(e.time || "—") + "</span>"],
        ["EXT", "<span class='mono'>" + esc(e.ext || "") + "</span>"],
        ["STATUS", e.gone ? "DELETED SINCE" : "PRESENT"],
        ["REASON", esc(e.reason || "")],
        ["USN", "<span class='mono'>" + esc(e.usn) + "</span>"]
      ])}
    ]);
  }

  /* ================= EVERYTHING ================= */
  var evTimer = null;
  function evShow(o) { $("evOut").textContent = JSON.stringify(o, null, 2); }
  function renderEvTiles(s) {
    function t(l, v) { return '<div class="tile"><small>' + l + "</small><b style='font-size:15px;word-break:break-all'>" + esc(v == null ? "—" : v) + "</b></div>"; }
    $("evTiles").innerHTML =
      t("INSTALLED", s.installed ? "YES" : "NO") +
      t("VERSION", s.version || "—") +
      t("LATEST KNOWN", s.latestKnown || "—") +
      t("SIZE", s.fileSizeLabel || "—") +
      t("INSTALL PATH", s.exePath || s.folder || "—");
    verdict("evBanner", !s.installed ? "bad" : s.updateAvailable ? "warn" : "ok",
      (!s.installed ? "■ NOT INSTALLED" : s.updateAvailable ? "■ UPDATE AVAILABLE" : "■ READY"), s.checkNote || "");
  }
  $("evStatus").addEventListener("click", function () {
    api("everything_status").then(function (s) { renderEvTiles(s); evShow(s); }).catch(function (e) { toast(String((e && e.message) || e)); });
  });
  $("evDownload").addEventListener("click", function () {
    Snd.arp();
    api("everything_download_start").then(function () {
      clearInterval(evTimer);
      evTimer = setInterval(function () {
        api("everything_progress").then(function (p) {
          $("evBar").style.width = (p.percent || 0) + "%";
          $("evStage").textContent = ((p.stage || "") + " (" + (p.percent || 0) + "%)").toUpperCase();
          if (p.done) {
            clearInterval(evTimer);
            evShow(p);
            if (p.ok) Snd.ok(); else Snd.err();
            api("everything_status").then(renderEvTiles).catch(function () {});
          }
        }).catch(function () { clearInterval(evTimer); });
      }, 800);
    }).catch(function (e) { toast(String((e && e.message) || e)); });
  });
  $("evCancel").addEventListener("click", function () {
    api("everything_cancel").then(function () { clearInterval(evTimer); toast("DOWNLOAD CANCELLED"); });
  });
  $("evLaunch").addEventListener("click", function () {
    api("everything_launch").then(function (p) { toast("LAUNCHED: " + p); }).catch(function (e) { toast(String((e && e.message) || e)); });
  });
  $("evFolder").addEventListener("click", function () {
    api("everything_open_folder").catch(function (e) { toast(String((e && e.message) || e)); });
  });
  $("evRemove").addEventListener("click", function () {
    api("everything_remove").then(function (m) { evShow(m); api("everything_status").then(renderEvTiles).catch(function () {}); })
      .catch(function (e) { toast(String((e && e.message) || e)); });
  });
  api("everything_status").then(renderEvTiles).catch(function () {});

  /* ================= TIMELINE + SETTINGS ================= */
  function parseStamp(s) {
    var m = /^(\d{4})-(\d{2})-(\d{2}) (\d{2}):(\d{2}):(\d{2})/.exec(s || "");
    if (!m) return 0;
    return new Date(+m[1], +m[2] - 1, +m[3], +m[4], +m[5], +m[6]).getTime();
  }
  function bootMs() {
    try {
      var r = lastResults.services;
      if (r && r.boot && r.boot.lastBoot) return parseStamp(r.boot.lastBoot);
    } catch (e) {}
    return 0;
  }
  function renderTimeline() {
    var st = chipState("tlMods");
    var sessionOnly = $("tlSession").checked;
    var max = parseInt($("tlMax").value, 10) || 1000;
    var boot = bootMs();
    var rows = [];
    function push(t, mod, label, sev) {
      if (!t) return;
      if (sessionOnly && boot && t < boot) return;
      rows.push({ t: t, mod: mod, label: label, sev: sev || "ok" });
    }
    try {
      var R = lastResults;
      if (st.services && R.services) {
        (R.services.services || []).forEach(function (s) {
          if (s.startedAt) push(parseStamp(s.startedAt), "SVC", "START " + (s.display || s.name), s.severity);
        });
        if (R.services.boot && R.services.boot.lastBoot && R.services.boot.tickMismatch)
          push(parseStamp(R.services.boot.lastBoot), "SVC", "BOOT (TICK MISMATCH)", "bad");
      }
      if (st.scheduler && R.scheduler) {
        (R.scheduler.entries || []).forEach(function (e) {
          push(e.createdUnix ? e.createdUnix * 1000 : parseStamp(e.created), "TASK", "CREATED " + e.name, e.severity);
        });
      }
      if (st.leftovers && R.leftovers) {
        ["tempFiles", "recentLinks", "dumps"].forEach(function (k) {
          ((R.leftovers || {})[k] || []).forEach(function (f) {
            push(parseStamp(f.modified), "LEFT", (f.kind || "FILE") + " " + f.name, f.severity);
          });
        });
      }
      if (st.prefetch && R.prefetch) {
        (R.prefetch.entries || []).forEach(function (e) {
          push(parseStamp(e.readableTime), "PF", "EXEC " + e.filename, (!e.isSigned || pfHits(e)) ? "warn" : "ok");
        });
      }
      if (st.amcache && R.amcache) {
        (R.amcache.entries || []).forEach(function (e) {
          push(e.keyUnix ? e.keyUnix * 1000 : parseStamp(e.keyTime), "AMC", "INVENTORY " + e.file, e.severity);
        });
      }
      if (st.bam && R.bam) {
        (R.bam.entries || []).forEach(function (e) {
          push(parseStamp(e.readableTime), "BAM", "EXEC " + e.path, (bamHasHit(e) || e.signature !== "Signed") ? "warn" : "ok");
        });
      }
      if (st.usn && R.usn) {
        (R.usn.entries || []).forEach(function (e) {
          push(e.unix ? e.unix * 1000 : parseStamp(e.time), "USN", (e.reason || "") + " " + e.name, "ok");
        });
      }
    } catch (e) {}
    rows.sort(function (a, b) { return b.t - a.t; });
    var shown = rows.slice(0, max);
    $("tlMeta").textContent = rows.length + " EVENTS" + (boot ? " · BOOT " + new Date(boot).toLocaleString() : "");
    if (!shown.length) { $("tlOut").innerHTML = '<div class="empty">NO DATA — RUN SCANS, THEN REFRESH.</div>'; return; }
    function fmt(t) {
      var d = new Date(t), p = function (n) { return (n < 10 ? "0" : "") + n; };
      return d.getFullYear() + "-" + p(d.getMonth() + 1) + "-" + p(d.getDate()) + " " +
        p(d.getHours()) + ":" + p(d.getMinutes()) + ":" + p(d.getSeconds());
    }
    var bootRow = "";
    if (boot) bootRow = "<tr><td class='mono'>" + esc(fmt(boot)) + "</td><td>BOOT</td><td>■ SESSION START</td></tr>";
    $("tlOut").innerHTML = "<table><thead><tr><th>TIME</th><th>MOD</th><th>EVENT</th></tr></thead><tbody>" + bootRow +
      shown.map(function (e) {
        return "<tr><td class='mono'>" + esc(fmt(e.t)) + "</td><td>" + sevSq(e.sev) + esc(e.mod) +
          "</td><td class='mono'>" + esc(e.label) + "</td></tr>";
      }).join("") + "</tbody></table>";
  }
  $("tlRefresh").addEventListener("click", function () { renderTimeline(); Snd.ok(); });
  function loadConfig() {
    api("config_get").then(function (raw) {
      var cfg = {};
      try { cfg = JSON.parse(raw || "{}"); } catch (e) {}
      if (cfg.caseDir) $("setDir").value = cfg.caseDir;
      if (cfg.tlSession !== undefined) $("tlSession").checked = !!cfg.tlSession;
      if (cfg.tlMax) $("tlMax").value = String(cfg.tlMax);
      if (cfg.tlModules) {
        document.querySelectorAll("#tlMods input").forEach(function (c) {
          if (cfg.tlModules[c.dataset.k] !== undefined) c.checked = !!cfg.tlModules[c.dataset.k];
        });
      }
    }).catch(function () {});
  }
  function saveConfig(silent) {
    var mods = {};
    document.querySelectorAll("#tlMods input").forEach(function (c) { mods[c.dataset.k] = c.checked; });
    var patch = {
      caseDir: $("setDir").value,
      tlModules: mods,
      tlSession: $("tlSession").checked,
      tlMax: parseInt($("tlMax").value, 10) || 1000
    };
    return api("config_set", patch).then(function (raw) {
      var ok = false;
      try { ok = !!JSON.parse(raw).ok; } catch (e) {}
      if (!silent) {
        $("setMsg").textContent = ok ? "SAVED" : "SAVE FAILED";
        toast(ok ? "SETTINGS SAVED" : "SETTINGS SAVE FAILED");
      }
      return ok;
    }).catch(function (e) { if (!silent) toast(String((e && e.message) || e)); return false; });
  }
  $("setSave").addEventListener("click", function () { saveConfig(false); });
  $("setBrowse").addEventListener("click", function () {
    api("config_browse").then(function (p) {
      if (p) {
        $("setDir").value = p;
        saveConfig(true).then(function () { $("setMsg").textContent = "SAVED"; });
      }
    }).catch(function (e) { toast(String((e && e.message) || e)); });
  });
  document.querySelectorAll("#tlMods input, #tlSession, #tlMax").forEach(function (c) {
    c.addEventListener("change", function () { saveConfig(true); renderTimeline(); });
  });
  loadConfig();

  /* ================= FIELD CHECKLIST ================= */
  var CHK = [
    { g: "PREP", id: "prep-admin", label: "Relaunched as admin", go: null },
    { g: "PREP", id: "prep-record", label: "Recording the session", go: null },
    { g: "SERVICES", id: "svc-run", label: "Service check run", go: "services" },
    { g: "SERVICES", id: "svc-bannable", label: "7 bannable services verified (threads checked in SI)", go: null },
    { g: "SERVICES", id: "svc-events", label: "Session signals + USB + AV reviewed", go: null },
    { g: "TRACES", id: "trace-pf", label: "Prefetch parsed + Amcache parsed", go: "prefetch" },
    { g: "TRACES", id: "trace-bam", label: "BAM parsed", go: "bam" },
    { g: "TRACES", id: "trace-usn", label: "USN journal walked", go: "usn" },
    { g: "IDENTITY", id: "id-alts", label: "Alts + HWID + mods recorded", go: "alts" },
    { g: "MEMORY", id: "mem-si", label: "SI nightly + driver on", go: "sysinfo" },
    { g: "MEMORY", id: "mem-cards", label: "DPS / CSRSS / PcaSvc memory swept", go: "guide" },
    { g: "PERSISTENCE", id: "persist", label: "Scheduler + leftovers swept", go: "services" },
    { g: "HUNT", id: "hunt-ev", label: "Everything queries run", go: "ev" },
    { g: "HUNT", id: "hunt-close", label: "Timeline reviewed + report saved", go: "time" }
  ];
  var chkState = {};
  var chkDay = "";
  function todayKey() { return new Date().toISOString().slice(0, 10); }
  function updCheckMeta() {
    var done = 0;
    CHK.forEach(function (it) { if (chkState[it.id]) done++; });
    $("chkMeta").textContent = done + " / " + CHK.length +
      (done === CHK.length ? " — ALL DONE" : "");
    $("chkBar").style.width = Math.round((done / CHK.length) * 100) + "%";
  }
  function paintCheck() {
    var lastG = "", h = "";
    CHK.forEach(function (it) {
      if (it.g !== lastG) {
        if (lastG) h += "</div>";
        h += '<div class="card"><h3>' + esc(it.g) + "</h3>";
        lastG = it.g;
      }
      var on = !!chkState[it.id];
      h += '<div class="chkrow"><input type="checkbox" data-chk="' + it.id + '"' + (on ? " checked" : "") + ">" +
        "<span>" + esc(it.label) + "</span>" +
        (it.go ? '<button class="btn sm" data-go="' + it.go + '">OPEN →</button>' : "") + "</div>";
    });
    h += "</div>";
    $("chkList").innerHTML = h;
    updCheckMeta();
    // In-place update on toggle — never re-render the row being clicked.
    $("chkList").querySelectorAll("[data-chk]").forEach(function (c) {
      c.addEventListener("change", function () {
        chkState[c.dataset.chk] = c.checked;
        updCheckMeta();
        saveCheck();
      });
    });
    $("chkList").querySelectorAll("[data-go]").forEach(function (b) {
      b.addEventListener("click", function (ev) { ev.stopPropagation(); });
    });
  }
  function saveCheck() {
    api("config_set", { checklist: chkState, checklistDay: chkDay }).catch(function () {});
  }
  function loadCheck() {
    api("config_get").then(function (raw) {
      var today = todayKey();
      try {
        var cfg = JSON.parse(raw || "{}");
        // Stale state from a previous day is dropped: every box is a fresh case.
        if (cfg.checklist && cfg.checklistDay === today) chkState = cfg.checklist;
      } catch (e) {}
      chkDay = today;
      paintCheck();
    }).catch(function () { chkDay = todayKey(); paintCheck(); });
  }
  $("chkReset").addEventListener("click", function () {
    chkState = {};
    chkDay = todayKey();
    saveCheck();
    paintCheck();
    toast("CHECKLIST RESET");
  });
  loadCheck();

  /* ================= MC PROFILES (settings) ================= */
  function renderMcProfiles() {
    api("mc_profiles").then(function (list) {
      list = list || [];
      $("mcProfList").innerHTML = list.map(function (p) {
        return "<div class='chkrow'><span>" + esc(p.path) +
          " <span class='meta'>" + esc(p.kind) + " · " + esc(p.source) +
          (p.lastUsed ? " · USED " + esc(p.lastUsed) : " · NEVER USED") +
          (p.exists ? "" : " · MISSING") + "</span></span>" +
          (p.source !== "AUTO" ? "<button class='btn sm danger' data-mcdel=\"" + esc(p.path) + "\">✕ REMOVE</button>" : "") +
          "</div>";
      }).join("") || "<div class='empty'>NO MINECRAFT DIRECTORIES FOUND — USE + ADD DIR… IF THE GAME LIVES SOMEWHERE UNUSUAL.</div>";
      var recent = list.filter(function (p) { return p.exists && p.lastUsed; }).length;
      $("mcMsg").textContent = list.length + " DIR" + (list.length === 1 ? "" : "S") +
        (recent > 1 ? " · ⚠ " + recent + " RECENTLY USED" : "");
      $("mcProfList").querySelectorAll("[data-mcdel]").forEach(function (b) {
        b.addEventListener("click", function () {
          api("mc_profile_remove", b.dataset.mcdel).then(function () {
            renderMcProfiles();
            toast("PROFILE REMOVED");
          }).catch(function (e) { toast(String((e && e.message) || e)); });
        });
      });
    }).catch(function () {
      $("mcProfList").innerHTML = "<div class='empty'>PROFILE SCAN FAILED</div>";
    });
  }
  $("mcRescan").addEventListener("click", function () {
    $("mcProfList").innerHTML = "<div class='empty'>SCANNING DRIVES…</div>";
    renderMcProfiles();
  });
  $("mcAdd").addEventListener("click", function () {
    api("config_browse").then(function (p) {
      if (!p) return;
      api("mc_profile_add", p).then(function (r) {
        if (r && r.ok === false) { toast(r.error || "ADD FAILED"); return; }
        toast("PROFILE ADDED");
        renderMcProfiles();
      }).catch(function (e) { toast(String((e && e.message) || e)); });
    }).catch(function (e) { toast(String((e && e.message) || e)); });
  });
  renderMcProfiles();

  /* ================= FULL CASE SCAN ================= */
  var CASEMODS = [
    { key: "services", label: "SERVICES", method: "service_checker_run", mkArgs: function () { return []; } },
    { key: "scheduler", label: "TASKS", method: "scheduler_run", mkArgs: function () { return []; } },
    { key: "leftovers", label: "LEFTOVERS", method: "leftover_run", mkArgs: function () { return []; } },
    { key: "prefetch", label: "PREFETCH", method: "prefetch_run", mkArgs: function () { return []; } },
    { key: "amcache", label: "AMCACHE", method: "amcache_run", mkArgs: function () { return []; } },
    { key: "bam", label: "BAM", method: "bam_run", mkArgs: function () { return []; } },
    { key: "alts", label: "ALTS", method: "alt_detector_run", mkArgs: function () { return []; } },
    { key: "usn", label: "USN", method: "usn_run", mkArgs: function () {
      return [{ paths: [($("usnDrive") && $("usnDrive").value) || "C:"] }];
    } }
  ];
  var caseCancelled = false;
  function paintCase(status) {
    var done = CASEMODS.filter(function (m) { return /^(DONE|FAIL|SKIPPED)/.test(status[m.key] || ""); }).length;
    var pct = Math.round((done / CASEMODS.length) * 100);
    var running = CASEMODS.some(function (m) { return status[m.key] === "RUNNING…"; });
    $("caseProg").innerHTML = '<div class="prog"><div id="caseBar" style="width:' + pct + '%"></div></div>' +
      '<div class="meta" style="margin-top:6px">' + (running ? "SCANNING… " : "") + done + " / " + CASEMODS.length + "</div>";
  }
  $("cancelCase").addEventListener("click", function () {
    caseCancelled = true;
    cancel().then(function () { toast("CASE SCAN CANCELLED"); });
  });
  var prevSnap = null;
  function snapKey(mod, e) {
    if (mod === "services") return "svc:" + (e.display || e.name);
    if (mod === "scheduler") return "task:" + e.name;
    if (mod === "leftovers") return "left:" + (e.path || e.name);
    if (mod === "prefetch") return "pf:" + e.filename;
    if (mod === "amcache") return "amc:" + (e.path || e.file);
    if (mod === "bam") return "bam:" + e.path;
    if (mod === "alts") return "alt:" + e;
    if (mod === "usn") return "usn:" + (e.path || e.name);
    return mod + ":" + JSON.stringify(e).slice(0, 120);
  }
  function snapSev(mod, e) {
    if (typeof e === "string") return "seen";
    if (mod === "prefetch") return (!e.isSigned || pfHits(e)) ? "warn" : "ok";
    if (mod === "bam") return (bamHasHit(e) || e.signature !== "Signed") ? "warn" : "ok";
    return e.severity || "ok";
  }
  function snapItems(mod) {
    var R = lastResults[mod];
    if (!R || R.error) return [];
    if (mod === "services") return R.services || [];
    if (mod === "scheduler") return R.entries || [];
    if (mod === "leftovers") return (R.tempFiles || []).concat(R.recentLinks || [], R.dumps || []);
    if (mod === "prefetch") return R.entries || [];
    if (mod === "amcache") return R.entries || [];
    if (mod === "bam") return R.entries || [];
    if (mod === "alts") return (R.minecraftAccounts || []).concat(R.discordIds || []);
    if (mod === "usn") return R.entries || [];
    return [];
  }
  function buildSnapshot() {
    var mods = {};
    ["services", "scheduler", "leftovers", "prefetch", "amcache", "bam", "alts", "usn"].forEach(function (mod) {
      var map = {};
      snapItems(mod).forEach(function (e) { map[snapKey(mod, e)] = snapSev(mod, e); });
      mods[mod] = map;
    });
    if (lastResults.shot && lastResults.shot.path)
      mods.shot = { ["shot:" + lastResults.shot.path]: "seen" };
    return { t: Date.now(), mods: mods };
  }
  function diffSnaps(prev, curr) {
    var added = [], removed = [];
    ["services", "scheduler", "leftovers", "prefetch", "amcache", "bam", "alts", "usn", "shot"].forEach(function (mod) {
      var p = (prev.mods || {})[mod] || {}, c = (curr.mods || {})[mod] || {};
      Object.keys(c).forEach(function (k) {
        if (!(k in p)) added.push({ mod: mod, key: k, sev: c[k] });
      });
      Object.keys(p).forEach(function (k) {
        if (!(k in c)) removed.push({ mod: mod, key: k, sev: p[k] });
      });
    });
    return { added: added.slice(0, 200), removed: removed.slice(0, 200) };
  }
  function collectHashPaths() {
    var out = [];
    function push(p) { if (p && out.indexOf(p) < 0) out.push(p); }
    var R = lastResults;
    if (R.prefetch) (R.prefetch.entries || []).forEach(function (e) { if (!e.isSigned && e.properPath) push(e.properPath); });
    if (R.bam) (R.bam.entries || []).forEach(function (e) { if (e.signature !== "Signed") push(e.path); });
    if (R.amcache) (R.amcache.entries || []).forEach(function (e) { if (e.severity !== "ok" && e.path) push(e.path); });
    if (R.leftovers) (R.leftovers.tempFiles || []).forEach(function (f) { if (f.severity !== "ok") push(f.path); });
    return out.slice(0, 100);
  }
  $("runCase").addEventListener("click", function () {
    caseCancelled = false;
    setBusy($("runCase"), true);
    $("caseReport").disabled = true;
    $("caseTiles").innerHTML = "";
    $("caseMeta").textContent = "RUNNING…";
    var status = {};
    paintCase(status);
    Snd.arp();
    api("report_read", "snapshot-latest.json").then(function (raw) {
      try { prevSnap = JSON.parse(raw); } catch (e) { prevSnap = null; }
    }).catch(function () { prevSnap = null; }).then(function () {
      var chain = Promise.resolve();
      CASEMODS.forEach(function (m) {
        chain = chain.then(function () {
          if (caseCancelled) { status[m.key] = "SKIPPED"; paintCase(status); return; }
          status[m.key] = "RUNNING…"; paintCase(status);
          return api.apply(null, [m.method].concat(m.mkArgs())).then(function (r) {
            if (r && r.error && !lastResults[m.key]) {
              status[m.key] = "FAIL"; paintCase(status);
              toast(m.label + ": " + r.error);
              return;
            }
            // Scanner-only: store results, touch nothing else. Views refresh
            // on demand when their own RUN button is pressed.
            lastResults[m.key] = r;
            if (r && typeof r.admin === "boolean") setAdmin(true, r.admin);
            var fc = (r && r.findingCount) || 0;
            status[m.key] = "DONE " + fc; paintCase(status);
          }).catch(function (e) {
            status[m.key] = "FAIL"; paintCase(status);
            toast(m.label + ": " + String((e && e.message) || e));
          });
        });
      });
      chain = chain.then(function () {
        if (caseCancelled) { finishCase(status, true); return; }
        $("caseMeta").textContent = "HASHING…";
        return api("hash_run", { paths: collectHashPaths() }).then(function (res) {
          lastResults.hashes = res || [];
        }).catch(function () { lastResults.hashes = []; }).then(function () {
          $("caseMeta").textContent = "SCREENSHOT…";
          return api("shot_run").then(function (shot) {
            lastResults.shot = shot || null;
          }).catch(function () { lastResults.shot = null; }).then(function () {
            finishCase(status, false);
          });
        });
      });
    });
  });
  function finishCase(status, cancelled) {
    var curr = buildSnapshot();
    var changes = prevSnap ? diffSnaps(prevSnap, curr) : null;
    lastResults._changes = changes;
    if (!cancelled) {
      api("report_save", JSON.stringify(curr), "snapshot-latest.json").catch(function () {});
    }
    setBusy($("runCase"), false);
    var total = 0, warnTotal = 0;
    var tiles = CASEMODS.map(function (m) {
      var r = lastResults[m.key];
      var fc = (r && r.findingCount) || 0;
      total += fc;
      return tile(m.label, fc, fc === 0 ? "lime" : "amber");
    }).join("");
    warnTotal = countWarns();
    $("caseTiles").innerHTML = tile("TOTAL", total, total === 0 ? "lime" : "red") + tiles;
    animateNums($("caseTiles"));
    var d = changes ? " · Δ +" + changes.added.length + " −" + changes.removed.length : "";
    $("caseMeta").textContent = cancelled ? "CANCELLED" : "COMPLETE · TOTAL " + total + d;
    $("caseReport").disabled = false;
    Snd.ok();
    renderTimeline();
  }
  function countWarns() {
    var n = 0, R = lastResults;
    function sev(s) { return s === "warn" || s === "bad"; }
    try {
      if (R.services) (R.services.services || []).forEach(function (s) { if (sev(s.severity)) n++; });
      if (R.scheduler) (R.scheduler.entries || []).forEach(function (e) { if (sev(e.severity)) n++; });
      if (R.leftovers) ["tempFiles", "recentLinks", "dumps"].forEach(function (k) {
        ((R.leftovers || {})[k] || []).forEach(function (f) { if (sev(f.severity)) n++; });
      });
      if (R.prefetch) (R.prefetch.entries || []).forEach(function (e) { if (!e.isSigned || pfHits(e)) n++; });
      if (R.amcache) (R.amcache.entries || []).forEach(function (e) { if (sev(e.severity)) n++; });
      if (R.bam) (R.bam.entries || []).forEach(function (e) { if (bamHasHit(e) || e.signature !== "Signed") n++; });
    } catch (e) {}
    return n;
  }

  /* ================= CASE REPORT (HTML FILE) ================= */
  var REPORT_CSS = ":root{--bg:#0a0c0d;--bg2:#0d1113;--panel:#111517;--panel2:#151b1e;--line:#232b2d;--line2:#394446;" +
    "--ink:#edf2e9;--mut:#8d978c;--lime:#c9f24b;--amber:#ffb02e;--red:#ff5a36;--ok:#35c26e;" +
    "--r:12px;--r-sm:8px;--font:'Segoe UI',system-ui,sans-serif;--mono:Consolas,monospace}" +
    "*{box-sizing:border-box}html,body{height:100%}body{margin:0;font-family:var(--font);background:var(--bg);color:var(--ink);font-size:14px;display:flex}" +
    "#side{width:264px;flex:none;background:var(--bg2);border-right:1px solid var(--line);display:flex;flex-direction:column;height:100vh;position:sticky;top:0;transition:margin .18s ease}" +
    "body.nav-closed #side{margin-left:-264px}" +
    ".s-brand{padding:16px;border-bottom:1px solid var(--line);font-weight:800;letter-spacing:.14em}" +
    ".s-brand .m{display:inline-flex;width:26px;height:26px;background:var(--lime);color:#0a0c0d;border-radius:6px;align-items:center;justify-content:center;margin-right:8px}" +
    ".s-brand small{display:block;color:var(--mut);font-size:10px;letter-spacing:.2em;margin-top:4px}" +
    "#q{margin:12px;padding:8px 10px;background:var(--panel);border:1px solid var(--line2);color:var(--ink);border-radius:var(--r-sm);font-family:var(--mono);font-size:12px}" +
    "#q:focus{outline:none;border-color:var(--lime)}" +
    "#side nav{flex:1;overflow:auto;padding:0 8px 12px}" +
    ".ng{font-size:10px;letter-spacing:.22em;color:var(--mut);padding:12px 8px 4px}" +
    "#side nav a{display:block;color:var(--mut);text-decoration:none;font-size:13px;padding:7px 10px;border-radius:var(--r-sm);border-left:2px solid transparent}" +
    "#side nav a:hover{color:var(--ink);background:var(--panel)}" +
    "#side nav a.active{color:var(--lime);border-left-color:var(--lime);background:var(--panel)}" +
    ".s-foot{padding:12px 16px;border-top:1px solid var(--line);color:var(--mut);font-size:11px}" +
    "#main{flex:1;min-width:0;padding:0 28px 60px;max-width:1180px}" +
    "#topbar{display:flex;align-items:center;gap:10px;padding:16px 0;border-bottom:1px solid var(--line);position:sticky;top:0;background:var(--bg);z-index:5}" +
    ".crumbs{font-family:var(--mono);font-size:12px;color:var(--mut);letter-spacing:.14em}" +
    ".crumbs b{color:var(--lime)}.spacer{flex:1}" +
    ".btn{background:transparent;color:var(--ink);border:1px solid var(--line2);border-radius:var(--r-sm);padding:7px 13px;cursor:pointer;font-size:12px;font-weight:700}" +
    ".btn:hover{border-color:var(--lime);color:var(--lime)}" +
    ".hero{background:var(--panel);border:1px solid var(--line2);border-radius:16px;padding:24px;margin:20px 0}" +
    ".vlabel{font-size:11px;letter-spacing:.24em;color:var(--mut)}.vword{font-size:46px;font-weight:800;letter-spacing:.05em}" +
    ".vword.clean{color:var(--ok)}.vword.susp{color:var(--amber)}.vword.det{color:var(--red)}" +
    ".rbar{height:10px;background:var(--bg2);border:1px solid var(--line2);border-radius:8px;overflow:hidden;margin:12px 0 4px}" +
    ".rbar div{height:100%;background:var(--lime)}" +
    ".htiles{display:flex;gap:10px;flex-wrap:wrap;margin-top:14px}" +
    ".htile{background:var(--bg2);border:1px solid var(--line);border-radius:10px;padding:10px 16px;font-size:11px;color:var(--mut);letter-spacing:.12em}" +
    ".htile b{font-size:22px;display:block;color:var(--ink)}" +
    "section{margin:26px 0}section>h2{font-size:16px;letter-spacing:.14em;text-transform:uppercase;color:var(--lime);margin:0 0 4px}" +
    ".meta{color:var(--mut);font-size:13px}" +
    "table{width:100%;border-collapse:collapse;font-size:12px;margin:10px 0;background:var(--panel);border:1px solid var(--line);border-radius:var(--r);overflow:hidden}" +
    "th,td{text-align:left;padding:8px 10px;border-bottom:1px solid var(--line);vertical-align:top}" +
    "th{color:var(--mut);font-size:10px;letter-spacing:.18em}.mono{font-family:var(--mono);word-break:break-all}" +
    ".bad{color:var(--red);font-weight:700}.warn{color:var(--amber);font-weight:700}.ok{color:var(--ok)}" +
    ".pill{display:inline-block;font-size:10px;font-weight:800;padding:2px 9px;border-radius:12px;border:1px solid var(--line2);color:var(--mut)}" +
    ".pill.bad{color:var(--red);border-color:var(--red)}.pill.warn{color:var(--amber);border-color:var(--amber)}.pill.ok{color:var(--ok);border-color:var(--ok)}" +
    "details{background:var(--panel);border:1px solid var(--line);border-radius:var(--r);margin:10px 0}" +
    "summary{cursor:pointer;padding:12px 16px;font-weight:700;list-style:none}" +
    "summary::-webkit-details-marker{display:none}summary:before{content:'▸ ';color:var(--lime)}details[open] summary:before{content:'▾ '}" +
    "details .dbody{padding:0 16px 12px}" +
    "img.shot{max-width:100%;border:1px solid var(--line2);border-radius:var(--r)}" +
    "@media(max-width:900px){#side{position:fixed;z-index:20;height:100vh}body.nav-closed #side{margin-left:-264px}#main{padding:0 14px 40px}}" +
    "@media print{#side,#topbar .btn,#q{display:none}body{background:#fff;color:#000}#main{max-width:none}details{break-inside:avoid}}";
  function repRow(cells, cls) {
    return "<tr" + (cls ? ' class="' + cls + '"' : "") + ">" +
      cells.map(function (c) { return "<td>" + c + "</td>"; }).join("") + "</tr>";
  }
  function repTable(heads, rows) {
    return "<table><thead><tr>" + heads.map(function (h) { return "<th>" + h + "</th>"; }).join("") +
      "</tr></thead><tbody>" + (rows.join("") || '<tr><td colspan="9">NO DATA</td></tr>') + "</tbody></table>";
  }
  function sevTxt(s) {
    return s === "bad" ? '<span class="bad">BAD</span>' : s === "warn" ? '<span class="warn">WARN</span>' : '<span class="ok">OK</span>';
  }
  var REPORT_JS = "var t=document.getElementById('navToggle'),q=document.getElementById('q');" +
    "t.onclick=function(){document.body.classList.toggle('nav-closed')};" +
    "document.getElementById('expandAll').onclick=function(){document.querySelectorAll('details').forEach(function(d){d.open=true})};" +
    "document.getElementById('collapseAll').onclick=function(){document.querySelectorAll('details').forEach(function(d){d.open=false})};" +
    "q.oninput=function(){var v=q.value.toLowerCase();document.querySelectorAll('#main table tbody tr').forEach(function(r){r.style.display=!v||r.textContent.toLowerCase().indexOf(v)>=0?'':'none'})};" +
    "var secs=[].slice.call(document.querySelectorAll('#main section[id]'));var links={};document.querySelectorAll('#side nav a').forEach(function(a){links[a.getAttribute('href').slice(1)]=a});" +
    "if('IntersectionObserver' in window){var io=new IntersectionObserver(function(es){es.forEach(function(e){if(e.isIntersecting){for(var k in links)links[k].classList.remove('active');var a=links[e.target.id];if(a)a.classList.add('active')}})},{rootMargin:'-40% 0px -55% 0px'});secs.forEach(function(s){io.observe(s)})}";
  function buildReport() {
    var R = lastResults, H = [];
    var DET = [], WRN = [];
    function det(src, label) { DET.push({ src: src, label: label }); }
    function wrn(src, label) { WRN.push({ src: src, label: label }); }
    try {
      var rs = R.services;
      if (rs && !rs.error) {
        (rs.services || []).forEach(function (s) {
          if (s.severity === "bad") det("SERVICES", s.display || s.name);
          else if (s.severity === "warn") wrn("SERVICES", s.display || s.name);
        });
        if (rs.events && !rs.events.skipped) {
          (rs.events.clears || []).forEach(function (c) { if (c.detected) det("LOGS", c.key); });
          [rs.events.usnDeleted, rs.events.serviceInstalled].forEach(function (s) {
            if (s && s.detected) det("LOGS", s.key);
          });
          [rs.events.logClosed, rs.events.logFull].forEach(function (s) {
            if (s && s.detected) wrn("LOGS", s.key);
          });
        }
      }
      if (R.scheduler && !R.scheduler.error) (R.scheduler.entries || []).forEach(function (e) {
        if (e.severity === "bad") det("TASKS", e.name);
        else if (e.severity === "warn") wrn("TASKS", e.name);
      });
      if (R.leftovers && !R.leftovers.error) {
        ["tempFiles", "recentLinks", "dumps"].forEach(function (k) {
          ((R.leftovers || {})[k] || []).forEach(function (f) {
            if (f.severity === "bad") det("LEFTOVERS", f.name);
            else if (f.severity === "warn") wrn("LEFTOVERS", f.name);
          });
        });
        ((R.leftovers || {}).hosts || []).forEach(function (x) {
          if (x.severity === "bad") det("HOSTS", x.ip + " " + x.host);
        });
        ((R.leftovers || {}).history || []).forEach(function (x) {
          if (x.severity === "bad") det("PS-HISTORY", x.preview.slice(0, 80));
          else wrn("PS-HISTORY", x.preview.slice(0, 80));
        });
      }
      if (R.prefetch && !R.prefetch.error) (R.prefetch.entries || []).forEach(function (e) {
        var hits = (e.matchedRules || []).some(function (x) { return x !== "none"; });
        if (hits) det("PREFETCH", e.filename);
        else if (!e.isSigned) wrn("PREFETCH", e.filename);
      });
      if (R.amcache && !R.amcache.error) (R.amcache.entries || []).forEach(function (e) {
        if (e.severity === "bad") det("AMCACHE", e.file);
        else if (e.severity === "warn") wrn("AMCACHE", e.file);
      });
      if (R.bam && !R.bam.error) (R.bam.entries || []).forEach(function (e) {
        var hits = (e.matchedRules || []).some(function (x) { return x !== "none"; }) ||
          (e.replaceResults || []).length > 0;
        if (hits) det("BAM", e.path);
        else if (e.signature !== "Signed") wrn("BAM", e.path);
      });
      if (R.alts && !R.alts.error && (R.alts.minecraftAccounts || []).length > 1)
        wrn("ALTS", R.alts.minecraftAccounts.length + " MINECRAFT ACCOUNTS");
      if (R.alts && !R.alts.error) {
        var activeProf = (R.alts.profiles || []).filter(function (p) { return p.exists && p.lastUsed; });
        if (activeProf.length > 1)
          wrn("MC", activeProf.length + " ACTIVE MINECRAFT PROFILES — possible hidden instance");
      }
      if (R.usn && !R.usn.error) (R.usn.entries || []).forEach(function (e) {
        if (e.gone) wrn("USN", "DELETED " + e.name);
      });
    } catch (e) {}
    var risk = Math.min(100, DET.length * 10 + WRN.length * 3);
    var word = DET.length ? "DETECTIONS" : (WRN.length || risk > 0) ? "SUSPICIOUS" : "CLEAN";
    var wcls = DET.length ? "det" : (WRN.length || risk > 0) ? "susp" : "clean";
    var NAV = [["summary", "Summary", "VERDICT"], ["detections", "Detections", "FINDINGS"],
      ["warnings", "Warnings", "FINDINGS"], ["environment", "Environment", "FINDINGS"],
      ["packed", "Packed binaries", "FINDINGS"], ["dupwarn", "Duplicates / renamed", "FINDINGS"],
      ["hashes", "File hashes", "FINDINGS"], ["screenshot", "Screenshot", "FINDINGS"],
      ["changes", "Changes", "FINDINGS"], ["services", "Services", "MODULES"],
      ["scheduler", "Tasks", "MODULES"], ["leftovers", "Leftovers", "MODULES"],
      ["prefetch", "Prefetch", "MODULES"], ["amcache", "Amcache", "MODULES"],
      ["bam", "BAM", "MODULES"], ["alts", "Alts", "MODULES"], ["usn", "USN", "MODULES"]];
    H.push("<!DOCTYPE html><html lang='en'><head><meta charset='utf-8'><meta name='viewport' content='width=device-width,initial-scale=1'>" +
      "<title>Maxim SS Tools — Case Report</title><style>" + REPORT_CSS + "</style></head><body>");
    H.push("<aside id='side'><div class='s-brand'><span class='m'>M</span>MAXIM<small>CASE REPORT</small></div>" +
      "<input id='q' placeholder='SEARCH…'>" +
      "<nav>" + (function () {
        var lastG = "", o = "";
        NAV.forEach(function (n) {
          if (n[2] !== lastG) { o += "<div class='ng'>" + n[2] + "</div>"; lastG = n[2]; }
          o += "<a href='#sec-" + n[0] + "'>" + n[1] + "</a>";
        });
        return o;
      })() + "</nav><div class='s-foot'>Maxim SS Tools v1.0.0 · offline</div></aside>");
    H.push("<div id='main'><div id='topbar'><button class='btn' id='navToggle'>☰</button>" +
      "<span class='crumbs'>CASE / <b>FULL REPORT</b></span><span class='spacer'></span>" +
      "<button class='btn' id='expandAll'>EXPAND</button>" +
      "<button class='btn' id='collapseAll'>COLLAPSE</button>" +
      "<button class='btn' onclick='window.print()'>PRINT</button></div>");
    H.push("<section id='sec-summary'><div class='hero'><div class='vlabel'>VERDICT</div><div class='vword " + wcls + "'>" + word + "</div>" +
      "<div class='rbar'><div style='width:" + risk + "%'></div></div><span class='meta'>RISK " + risk +
      " / 100 · " + DET.length + " detections · " + WRN.length + " warnings · formula: bad×10 + warn×3</span>" +
      "<div class='htiles'><div class='htile'><b>" + DET.length + "</b>DETECTIONS</div>" +
      "<div class='htile'><b>" + WRN.length + "</b>WARNINGS</div>" +
      "<div class='htile'><b>" + risk + "</b>RISK</div></div></div>" +
      "<p class='meta'>Generated " + esc(new Date().toLocaleString()) + " · Maxim SS Tools v1.0.0 · local offline report</p></section>");
    H.push("<section id='sec-detections'><details open><summary>Detections (" + DET.length + ")</summary>" +
      repTable(["SOURCE", "ITEM"], DET.slice(0, 300).map(function (d) {
        return repRow(['<span class="bad">●</span> ' + esc(d.src), esc(d.label)]);
      })) + "</details></section>");
    H.push("<section id='sec-warnings'><details><summary>Warnings (" + WRN.length + ")</summary>" +
      repTable(["SOURCE", "ITEM"], WRN.slice(0, 300).map(function (d) {
        return repRow(['<span class="warn">●</span> ' + esc(d.src), esc(d.label)]);
      })) + "</details></section>");
    var envRows = [];
    if (R.services && !R.services.error) {
      envRows.push(repRow(["VIRTUAL MACHINE", esc(R.services.vmDetected ? "YES — " + (R.services.vmVendor || "") : "NO" + (R.services.vmVendor ? " (" + R.services.vmVendor + ")" : ""))]));
      ((R.services.events || {}).avProducts || []).forEach(function (a) {
        envRows.push(repRow(["ANTIVIRUS", esc(a.name) + " — " + esc(a.state)]));
      });
    }
    if (R.alts && !R.alts.error && R.alts.hwid) {
      envRows.push(repRow(["MACHINE GUID", esc(R.alts.hwid.machineGuid || "—")]));
      (R.alts.hwid.disks || []).forEach(function (d) { envRows.push(repRow(["DISK", esc(d)])); });
    }
    if (R.services && R.services.events && !R.services.events.skipped) {
      ((R.services.events || {}).usbDevices || []).slice(0, 30).forEach(function (u) {
        envRows.push(repRow(["USB", esc(u.name) + " [" + esc(u.serial) + "]"]));
      });
    }
    H.push("<section id='sec-environment'><details><summary>Environment</summary>" + repTable(["KEY", "VALUE"], envRows) + "</details></section>");
    var PACKIDS = ["B", "B2", "B3", "B4", "B5", "B6", "B7", "F", "F2", "F3", "F4", "F5", "F6", "F7", "E", "sB"];
    var packRows = [];
    [["prefetch", "PREFETCH"], ["bam", "BAM"]].forEach(function (pair) {
      var rr = R[pair[0]];
      if (!rr || rr.error) return;
      (rr.entries || []).forEach(function (e) {
        (e.matchedDetails || []).forEach(function (d) {
          if (PACKIDS.indexOf(d.id) >= 0)
            packRows.push(repRow([esc(pair[1]), esc(e.filename || e.path || ""), esc(d.id + " · " + (d.name || "")),
              esc((d.sample || "").slice(0, 80))]));
        });
      });
    });
    H.push("<section id='sec-packed'><details><summary>Packed / obfuscated binaries (" + packRows.length + ")</summary>" +
      repTable(["SOURCE", "FILE", "RULE", "SAMPLE"], packRows.slice(0, 300)) + "</details></section>");
    var dupRows = [], modextRows = [];
    try {
      if (R.prefetch && !R.prefetch.error) {
        var byStem = {};
        (R.prefetch.entries || []).forEach(function (e) {
          var stem = String(e.filename || "").split("-")[0];
          if (!stem) return;
          byStem[stem] = byStem[stem] || [];
          if (byStem[stem].indexOf(e.prefetchHash) < 0) byStem[stem].push(e.prefetchHash);
        });
        Object.keys(byStem).forEach(function (s) {
          if (byStem[s].length > 1)
            dupRows.push(repRow([esc(s), esc(byStem[s].join(", ")), esc(byStem[s].length + " variants")]));
        });
        (R.prefetch.entries || []).forEach(function (e) {
          var p = String(e.properPath || "");
          var m = /\.([A-Za-z0-9]+)$/.exec(p);
          if (p && m && [".exe", ".dll"].indexOf(m[0].toLowerCase()) < 0)
            modextRows.push(repRow([esc(e.filename), esc(p)]));
        });
      }
      if (R.bam && !R.bam.error) (R.bam.entries || []).forEach(function (e) {
        var p = String(e.path || "");
        var m = /\.([A-Za-z0-9]+)$/.exec(p);
        if (p && m && [".exe", ".dll"].indexOf(m[0].toLowerCase()) < 0)
          modextRows.push(repRow([esc(p.split("\\").pop()), esc(p)]));
      });
    } catch (e) {}
    H.push("<section id='sec-dupwarn'><details><summary>Duplicates / renamed extensions</summary>" +
      repTable(["STEM", "HASHES", "NOTE"], dupRows.slice(0, 200)) +
      repTable(["FILE", "NON EXE/DLL PATH"], modextRows.slice(0, 300)) + "</details></section>");
    if (R.hashes && R.hashes.length) {
      H.push("<section id='sec-hashes'><details><summary>File hashes (" + R.hashes.length + ")</summary>" +
        repTable(["PATH", "SHA256", "SIZE"], R.hashes.map(function (x) {
          return repRow([esc(x.path), x.sha256 ? esc(x.sha256) : esc(x.error || "—"), esc(x.size || "")]);
        })) + "</details></section>");
    }
    if (R.shot && R.shot.path) {
      H.push("<section id='sec-screenshot'><details open><summary>Last game screenshot</summary><p class='meta'>" +
        esc(R.shot.path) + " · " + esc(R.shot.modified || "") + " · " + esc(R.shot.sizeLabel || "") + "</p>" +
        (R.shot.image ? "<img class='shot' src='data:image/png;base64," + R.shot.image + "'>" :
          "<p class='meta'>Not embedded (too large).</p>") + "</details></section>");
    }
    if (R._changes && (R._changes.added.length || R._changes.removed.length)) {
      H.push("<section id='sec-changes'><details open><summary>Changes since last scan</summary>" +
        repTable(["DIR", "MODULE", "KEY"], R._changes.added.map(function (x) {
          return repRow(['<span class="warn">+ ADDED</span>', esc(x.mod), esc(x.key)]);
        }).concat(R._changes.removed.map(function (x) {
          return repRow(['<span class="bad">− GONE</span>', esc(x.mod), esc(x.key)]);
        }))) + "</details></section>");
    }
    function modSec(key, title, stamp, body) {
      H.push("<section id='sec-" + key + "'><h2>" + title + "</h2><p class='meta'>" + stamp +
        "</p><details><summary>VIEW DETAILS</summary><div class='dbody'>" + body + "</div></details></section>");
    }
    var r = R.services;
    if (r && !r.error) {
      var rows = (r.services || []).map(function (s) {
        return repRow([sevTxt(s.severity), esc(s.display || s.name), esc(s.status), esc(s.startType || ""), esc(s.pid || "")]);
      });
      var ev2 = r.events && !r.events.skipped
        ? ((r.events.clears || []).map(function (c) {
            return repRow([sevTxt(c.detected ? "bad" : "ok"), esc(c.key), esc(c.detected ? "CLEARED " + (c.when || "") : "OK")]);
          }).join("") + [r.events.usnDeleted, r.events.logClosed, r.events.logFull, r.events.serviceInstalled,
            r.events.procCreated, r.events.usbIn, r.events.usbOut].filter(Boolean).map(function (s) {
            return repRow([sevTxt(s.detected ? s.severity : "ok"), esc(s.key), esc(s.detected ? (s.when || "") : "NO DATA")]);
          }).join(""))
        : "";
      modSec("services", "Service Checker — findings " + (r.findingCount || 0),
        "Boot " + esc((r.boot && r.boot.lastBoot) || "?"),
        repTable(["SEV", "SERVICE", "STATUS", "START", "PID"], rows) +
        (ev2 ? "<h2>Event signals</h2>" + repTable(["SEV", "SIGNAL", "STATE"], [ev2]) : "") +
        (((r.events || {}).usbDevices || []).length ? repTable(["USB DEVICE", "SERIAL"],
          r.events.usbDevices.map(function (u) { return repRow([esc(u.name), esc(u.serial)]); })) : "") +
        (((r.events || {}).avProducts || []).length ? repTable(["AV PRODUCT", "STATE", "SOURCE"],
          r.events.avProducts.map(function (a) { return repRow([esc(a.name), esc(a.state), esc(a.source)]); })) : ""));
    }
    r = R.scheduler;
    if (r && !r.error) {
      modSec("scheduler", "Scheduled Tasks — findings " + (r.findingCount || 0), (r.entries || []).length + " tasks",
        repTable(["SEV", "TASK", "TRIGGERS", "ACTION", "CREATED"], (r.entries || []).map(function (e) {
          return repRow([sevTxt(e.severity), esc(e.name) + "<br><span class='meta'>" + esc(e.folder || "") + "</span>",
            esc((e.triggers || []).join(" · ")), esc(e.action || "") + (e.args ? "<br>" + esc(e.args) : ""), esc(e.created || "")]);
        })));
    }
    r = R.leftovers;
    if (r && !r.error) {
      var lr = [];
      ["tempFiles", "recentLinks", "dumps"].forEach(function (k) {
        ((r || {})[k] || []).forEach(function (f) {
          lr.push(repRow([sevTxt(f.severity), esc(f.name) + "<br><span class='meta'>" + esc(f.path || "") + "</span>",
            esc(f.sizeLabel || ""), esc(f.modified || "")]));
        });
      });
      var hr = ((r || {}).hosts || []).map(function (x) {
        return repRow([sevTxt(x.severity), esc(x.ip), esc(x.host)]);
      });
      var hr2 = ((r || {}).history || []).map(function (x) {
        return repRow([sevTxt(x.severity), esc(x.preview)]);
      });
      modSec("leftovers", "Leftovers — findings " + (r.findingCount || 0), "",
        repTable(["SEV", "ITEM", "SIZE", "MODIFIED"], lr) +
        repTable(["SEV", "IP", "HOST"], hr) +
        repTable(["SEV", "PS HISTORY HIT"], hr2));
    }
    r = R.prefetch;
    if (r && !r.error) {
      modSec("prefetch", "Prefetch — findings " + (r.findingCount || 0), (r.filesFound || 0) + " files",
        repTable(["FILE", "PATH", "LAST RUN", "SIGNED", "RULES"], (r.entries || []).map(function (e) {
          return repRow([esc(e.filename), esc(e.properPath || ""), esc(e.readableTime || ""),
            e.isSigned ? "YES" : "NO", esc((e.matchedRules || []).join(", "))]);
        })));
    }
    r = R.amcache;
    if (r && !r.error) {
      modSec("amcache", "Amcache — findings " + (r.findingCount || 0), (r.keysScanned || 0) + " keys",
        repTable(["SEV", "FILE", "PATH", "KEY TIME"], (r.entries || []).map(function (e) {
          return repRow([sevTxt(e.severity), esc(e.file), esc(e.path || ""), esc(e.keyTime || "")]);
        })));
    }
    r = R.bam;
    if (r && !r.error) {
      modSec("bam", "BAM — findings " + (r.findingCount || 0), (r.entries || []).length + " entries",
        repTable(["PATH", "LAST EXEC", "SIG", "RULES"], (r.entries || []).map(function (e) {
          return repRow([esc(e.path), esc(e.readableTime || ""), esc(e.signature || ""), esc((e.matchedRules || []).join(", "))]);
        })));
    }
    r = R.alts;
    if (r && !r.error) {
      modSec("alts", "Alt Checker", "",
        repTable(["MINECRAFT ACCOUNTS"], (r.minecraftAccounts || []).map(function (u) {
          return repRow([esc(u)]);
        })) +
        repTable(["DISCORD ID"], (r.discordIds || []).map(function (id) { return repRow([esc(id)]); })) +
        ((r.mods || []).length ? repTable(["MOD JAR", "PATH", "SIZE"], r.mods.map(function (m) {
          return repRow([esc(m.name), esc(m.path || ""), esc(m.sizeLabel || "")]);
        })) : ""));
    }
    r = R.usn;
    if (r && !r.error) {
      modSec("usn", "USN Explorer — drive " + esc(r.drive || "?"), (r.scanned || 0) + " records",
        repTable(["TIME", "NAME", "PATH", "REASON"], (r.entries || []).map(function (e) {
          return repRow([esc(e.time || ""), esc(e.name), esc(e.path || ""), esc(e.reason || "")]);
        })));
    }
    H.push("</div><script>" + REPORT_JS + "</scr" + "ipt></body></html>");
    return H.join("\n");
  }
  $("caseReport").addEventListener("click", function () {
    var btn = $("caseReport");
    setBusy(btn, true);
    try {
      var html = buildReport();
      api("report_save", html, "case-report.html").then(function (path) {
        $("caseMeta").textContent = "SAVED " + path;
        toast("REPORT SAVED");
        api("report_open", path).catch(function (e) { toast(String((e && e.message) || e)); });
      }).catch(function (e) { toast(String((e && e.message) || e)); })
        .finally(function () { setBusy(btn, false); });
    } catch (e) {
      setBusy(btn, false);
      toast("REPORT FAILED: " + e.message);
    }
  });

  /* ================= SYSTEM INFORMER ================= */
  var siTimer = null;
  function siShow(o) { $("siOut").textContent = JSON.stringify(o, null, 2); }
  function renderSiTiles(s) {
    function t(l, v) { return '<div class="tile"><small>' + l + "</small><b style='font-size:15px;word-break:break-all'>" + esc(v == null ? "—" : v) + "</b></div>"; }
    $("siTiles").innerHTML =
      t("INSTALLED", s.installed ? ("YES " + (s.version || s.installedTag || "")) : "NO") +
      t("LATEST", s.latestVersion || "UNKNOWN") +
      t("DOWNLOADED", s.lastDownload || "—") +
      t("SIZE", s.fileSizeLabel || "—") +
      t("INSTALL PATH", s.exePath || s.folder || "—") +
      t("LAST CHECK", s.lastCheckedAt || "—");
    var note = s.checkNote || (s.installed ? "READY" : "NOT INSTALLED — DOWNLOAD THE CANARY");
    verdict("siBanner", !s.installed ? "bad" : s.updateAvailable ? "warn" : "ok",
      (!s.installed ? "■ NOT INSTALLED" : s.updateAvailable ? "■ UPDATE AVAILABLE" : "■ READY"), note);
  }
  $("siStatus").addEventListener("click", function () {
    api("system_informer_status").then(function (s) { renderSiTiles(s); siShow(s); }).catch(function (e) { toast(String((e && e.message) || e)); });
  });
  $("siCheck").addEventListener("click", function () {
    api("system_informer_check_update").then(function (u) {
      siShow(u);
      api("system_informer_status").then(renderSiTiles).catch(function () {});
    }).catch(function (e) { toast(String((e && e.message) || e)); });
  });
  $("siDownload").addEventListener("click", function () {
    Snd.arp();
    api("system_informer_download_start").then(function () {
      clearInterval(siTimer);
      siTimer = setInterval(function () {
        api("system_informer_progress").then(function (p) {
          $("siBar").style.width = (p.percent || 0) + "%";
          $("siStage").textContent = ((p.stage || "") + " (" + (p.percent || 0) + "%)").toUpperCase();
          if (p.done) {
            clearInterval(siTimer);
            siShow(p);
            if (p.ok) Snd.ok(); else Snd.err();
            api("system_informer_status").then(renderSiTiles).catch(function () {});
          }
        }).catch(function () { clearInterval(siTimer); });
      }, 800);
    }).catch(function (e) { toast(String((e && e.message) || e)); });
  });
  $("siCancel").addEventListener("click", function () {
    api("system_informer_cancel").then(function () { clearInterval(siTimer); toast("DOWNLOAD CANCELLED"); });
  });
  $("siLaunch").addEventListener("click", function () {
    api("system_informer_launch").then(function (p) { toast("LAUNCHED: " + p); }).catch(function (e) { toast(String((e && e.message) || e)); });
  });
  $("siFolder").addEventListener("click", function () {
    api("system_informer_open_folder").catch(function (e) { toast(String((e && e.message) || e)); });
  });
  $("siRemove").addEventListener("click", function () {
    api("system_informer_remove").then(function (m) { siShow(m); api("system_informer_status").then(renderSiTiles).catch(function () {}); })
      .catch(function (e) { toast(String((e && e.message) || e)); });
  });
  api("system_informer_status").then(renderSiTiles).catch(function () {});

  /* ================= FIELD GUIDE (12 methods) ================= */
  var GUIDE = [
    { tag: "SETUP", hv: false, title: "Prepare the reader before the session",
      why: "System Informer replaces Process Hacker as the live process reader — free, deep, and able to inspect service memory on the spot with no captured sample. Only the nightly/canary channel carries the kernel-driver switch on Windows 11.",
      path: ["SI NIGHTLY (ADMIN)", "OPTIONS", "KERNEL DRIVER", "RESTART SI", "PROCESSES"],
      steps: ["Fetch the nightly/canary build from the SI tab and open it elevated.", "Flip the kernel driver on under Options, then fully close and reopen so it engages.", "Do the bulk of the work in Processes; open Disk for live recording or VPN suspicion, Devices for attached or once-seen drives.", "For each memory read use Properties, Memory, tick Hide free pages plus Hide reserved pages, then Strings at length 4 with Extended Unicode + Mapped + Image."],
      warn: "Driver-level poking can bluescreen the machine — run only the reads each card lists.",
      reds: ["Stable-channel build, driver off, or wrong Strings setup — every later card silently loses evidence."],
      normal: "Nightly build, driver live after restart, length 4 with Extended Unicode + Mapped + Image.",
      tip: "Record the whole session. Kill Defender and third-party AV first; type commands in CMD, never raw PowerShell.",
      filters: [] },
    { tag: "THREADS", hv: true, title: "Frozen services: the check that must not be skipped",
      why: "Seven services are an instant ban when stopped — PcaSvc, SysMain, DPS, numbered CDPUsvc entries, EventLog, DcomLaunch and the Activities Cache — but worse, targets only pause threads so everything still reports Running while logging nothing. A suspect running Hacker/SI on their own box is itself a ban sign.",
      path: ["SERVICE SVCHOST", "PROPERTIES", "THREADS", "START-ADDRESS DLL"],
      steps: ["Confirm none of the 7 bannable services is stopped or disabled, then look for restarts suspiciously close to the screenshare.", "In SI open each service-hosting svchost, Properties, Threads.", "Read the start-address column against the service core DLL mapping below.", "A Suspended thread rooted in the core DLL means silent freeze — flag it even if it says Running."],
      reds: ["Any of the 7 bannable services stopped/disabled.", "A Suspended thread inside the service core DLL.", "Restarts right before the SS, or the suspect running Hacker/SI themselves."],
      normal: "All 7 Running, threads live, uptimes stable.",
      tip: "A dead service can pass the automated service check — this card is the manual confirmation it cannot give.",
      filters: [
        { type: "Contains", label: "SysMain thread DLL", pattern: "sechost.dll" },
        { type: "Contains", label: "PcaSvc thread DLL", pattern: "pcasvc.dll" },
        { type: "Contains", label: "DPS thread DLL", pattern: "dps.dll" },
        { type: "Contains", label: "EventLog thread DLL", pattern: "wevtsvc.dll" },
        { type: "Contains", label: "dusmsvc thread DLL", pattern: "wlanapi.dll" },
        { type: "Contains", label: "cdpusersvc thread DLL", pattern: "ucrtbase.dll" } ] },
    { tag: "DPS", hv: true, title: "DPS memory: renamed launches and odd executions",
      why: "The Diagnostic Policy Service notices extension renames and executions alike, and its memory keeps the executed paths — sweeping it turns up modified-extension tricks and off-spec runs.",
      path: ["DPS SVCHOST (DPS.DLL)", "PROPERTIES", "MEMORY", "STRINGS"],
      steps: ["Find the DPS-hosting svchost (dps.dll).", "Right-click, Properties, Memory, Strings (length 4, Extended Unicode, Mapped, Image).", "Open with the Wiki modified-extension filter, then the three exclusion sweeps.", "Chase every non-.exe / non-.dll result and every localhost path."],
      reds: ["Hits on the Wiki modified-extension filter.", "Non-.exe / non-.dll runs, localhost paths.", "Payloads in Downloads, Temp, hidden folders."],
      normal: "Plain .exe / .dll under Program Files, Windows, game folders.",
      tip: "Lead with the Wiki filter, finish the sweeps, confirm each hit against BAM and Prefetch before calling it.",
      filters: [
        { type: "Regex", label: "Wiki · modified extension in DPS", pattern: "^!![A-Z]((?!Exe).)*$" },
        { type: "Regex", label: "Excludes .exe and .dll", pattern: "^\\\\device\\\\harddiskvolume((?!Exe|dll).)*$" },
        { type: "Regex", label: "Modified extension — non-.exe from a volume path", pattern: "^\\\\device\\\\harddiskvolume[0-99]\\\\((?!exe).)*$" },
        { type: "Regex", label: "Localhost executions", pattern: "^\\\\device\\\\mup\\\\localhost\\\\*" } ] },
    { tag: "CSRSS", hv: false, title: "CSRSS: pick the right twin for each hunt",
      why: "CSRSS observes every process birth on the box, recording creation tricks, hollowing leftovers and extension-rename dodges. Two instances exist — each check wants the correct one.",
      path: ["CSRSS.EXE (BOTH)", "PROPERTIES", "MEMORY", "STRINGS"],
      steps: ["Locate both csrss.exe processes and compare Private Bytes.", ".exe trails: the CSRSS with lower private bytes. Filter, save the dump, run Paths Parser.", ".dll trails: the CSRSS with higher private bytes. Same routine.", "Renamed-extension hunt: the CSRSS holding the most bytes.", "Unsigned-scan warm-up (Maceta flow): dump the fattest csrss with strings :\\ and .exe, keep the .txt, run the scanner elevated."],
      reds: ["A single csrss.exe, or one outside System32.", "Userland .exe strings, renamed-extension hits (.txt / .dat that ran code)."],
      normal: "Two System32 instances, no user strings, no renamed hits.",
      tip: "Low bytes is .exe activity, high bytes .dll. Archive every dump plus the .txt for the unsigned pass.",
      filters: [
        { type: "Regex", label: ".exe paths — lower private bytes CSRSS", pattern: "^[A-Z]:\\\\.+\\.(exe)$" },
        { type: "Regex", label: ".dll paths — higher private bytes CSRSS", pattern: "^[A-Z]:\\\\.+\\.(dll)$" },
        { type: "Regex", label: "Renamed extension — most-bytes CSRSS", pattern: "^[a-z]:.+\\.((?!exe|pyd|manifest|dll|config|\\\\|cpl|microsoft-|shell).)*$" } ] },
    { tag: "PCASVC", hv: false, title: "PcaSvc memory: runs from the wrong neighborhoods",
      why: "The Program Compatibility Assistant keeps its own ledger of launched applications — its memory strings betray binaries run from places they should never live, like Downloads or Temp.",
      path: ["PCASVC PROCESS", "PROPERTIES", "MEMORY", "STRINGS"],
      steps: ["Find the PcaSvc process in SI.", "Right-click, Properties, Memory, Strings (length 4, Extended Unicode, Mapped, Image).", "Look for .exe paths outside legit roots (Program Files, Windows, minecraft trees).", "Hold every odd path against BAM and DPS results."],
      reds: ["Any .exe in Downloads, Temp, hidden dirs, or outside install roots."],
      normal: "Install-root paths only; nothing out of user folders.",
      tip: "A path vouched for by two sources (here + BAM/DPS) is strong evidence; single-source hits stay suspects.",
      filters: [
        { type: "Regex", label: ".exe paths in memory", pattern: "^[A-Z]:\\\\.+\\.(exe)$" },
        { type: "Contains", label: "Case-insensitive · Downloads focus", pattern: "\\Downloads\\" },
        { type: "Contains", label: "Case-insensitive · Temp focus", pattern: "\\Temp\\" } ] },
    { tag: "SCHEDULER", hv: false, title: "Boot tasks that dodge every string log",
      why: "Jobs firing at boot or logon never pass through normal string logging — Scheduler memory is where they surface: creation XML, executed-file traces, journal names of deleted tasks.",
      path: ["SCHEDULER PROCESS", "MEMORY", "STRINGS", "</COMMAND> REVIEW"],
      steps: ["Filter Scheduler for computername= to learn the PC value; take the username from Task Manager or C:\\Users.", "Hunt creation XML with the COMPUTERNAME string (real PC and user).", "Sweep executed files with the regex (contains, case-insensitive) — flag .exe / .dll outside C:\\Windows\\System32.", "For vanished tasks: name from JournalTool (C:\\Windows\\System32\\Tasks), filter SI by it, export .txt, read the </Command> line."],
      reds: ["Schedule-executed .exe / .dll outside System32.", "Task XML with strange commands; deleted tasks near SS time."],
      normal: "Windows maintenance tasks with system paths only.",
      tip: "Only boot/logon-timed tasks count — skip hourly noise. The </Command> line is the verdict.",
      filters: [
        { type: "Contains", label: "Wiki · task XML artifacts (append PC\\user)", pattern: "COMPUTERNAME=" },
        { type: "Regex", label: "Wiki · files executed by Schedule", pattern: "([A-Z]:\\\\.+\\.(dll|exe)|\"[A-Z]:\\\\.+\\.(dll|exe)\")$" } ] },
    { tag: "INDEXER", hv: false, title: "Search Indexer: six-letter config traces",
      why: "The indexer hoards filename traces exposing cheat configs and scripts by shape — including Skript-style .json files the standard sweeps walk past.",
      path: ["SEARCH INDEXER", "MEMORY", "STRINGS"],
      steps: ["Open Search Indexer memory strings in SI.", "Apply the six-character .json pattern.", "Vet each hit for cheat configs or scripts."],
      reds: ["Six-character .json traces resolving to cheat configs/scripts."],
      normal: "No matching .json traces.",
      tip: "Lift the filename into Search Everything to learn where it lived.",
      filters: [
        { type: "Regex", label: "Wiki · 6-char Skript .json", pattern: "^[0-9a-zA-Z]{6}\\.json$" } ] },
    { tag: "SEARCH", hv: true, title: "Everything: filename hunting by date and keyword",
      why: "Everything indexes every filename on every disk in seconds — the fastest way to surface fresh executables and known cheat keywords without walking journals by hand.",
      path: ["EVERYTHING (ADMIN)", "SEARCH BAR", "SORT BY DATE MODIFIED"],
      steps: ["Fetch the portable build from the EV tab and open it elevated so all volumes index.", "Start with the date-bounded sweeps below, newest first.", "Then run the extension sweeps for today.", "Finish with the keyword list; every hit gets opened in its folder and cross-checked against BAM and Prefetch."],
      reds: ["Fresh .exe / .dll / .jar outside install roots.", "Keyword hits (clicker, inject, loader) anywhere user-writable.", "Renamed binaries hiding on side disks."],
      normal: "Only expected installers and program files, timestamps matching install dates.",
      tip: "Pair each search with Date Modified sort — a cheat renamed last week glows next to untouched system files.",
      filters: [
        { type: "Query", label: "Modified today", pattern: "dm:today" },
        { type: "Query", label: "Modified in the last 2 hours", pattern: "dm:last2hours" },
        { type: "Query", label: "Executables modified today", pattern: "ext:exe dm:today" },
        { type: "Query", label: "Libraries modified today", pattern: "ext:dll dm:today" },
        { type: "Query", label: "Jars modified today", pattern: "ext:jar dm:today" },
        { type: "Query", label: "Keyword sweep 1", pattern: "AClib" },
        { type: "Query", label: "Keyword sweep 2", pattern: "clicker" },
        { type: "Query", label: "Keyword sweep 3", pattern: "inject" },
        { type: "Query", label: "Keyword sweep 4", pattern: "loader" } ] },
    { tag: "DISKS", hv: false, title: "Off-C: hunting beyond the system drive",
      why: "Payloads on D:, E:, USB sticks or virtual disks sail past every C:-only check — pair this with Search Everything sorted by Date Modified.",
      path: ["SYSTEM INFORMER", "EXPLORER DUMP", "MEMORY", "STRINGS"],
      steps: ["Dump the target process (Explorer memory strings work too).", "Run the non-C filter (search inside Explorer).", "Treat every non-C: result as hostile until proven otherwise."],
      reds: ["Loaders/executables on D: / E: / USB / virtual disks — above all near the freeze."],
      normal: "System and program paths on C: only.",
      tip: "Cross-sort Search Everything by Date Modified to unmask renamed binaries on those disks.",
      filters: [
        { type: "Regex", label: "Wiki · disks other than C: (Explorer)", pattern: "^(?!C:)[A-Z]:[\\\\](?!\\+|(u){FFFF}|rkr)" } ] },
    { tag: "TAMPER", hv: true, title: "Scrubbed memory leaves a receipt",
      why: "Wiping cheat strings out of memory with SI or a sibling tool ahead of the screenshare leaves its own mark — ban-grade proof.",
      path: ["DWM / EXPLORER / TASKHOSTW", "MEMORY", "STRINGS"],
      steps: ["Open memory strings of dwm.exe, explorer.exe, taskhostw.exe.", "Run the residue pattern.", "A line like \"name.exe (4684) (0x195c66a3000 - 0x195c66c7000)\" proves hand-opened regions.", "Log name, PID, address range as evidence."],
      reds: ["Any \"name.exe (PID) (0x… - 0x…)\" hit — strings scrubbed pre-SS."],
      normal: "Zero \") (0x\" hits across the three processes.",
      tip: "Screenshot the full line — name, PID, range together justify a ban alone.",
      filters: [
        { type: "Contains", label: "Manual memory-access residue", pattern: ") (0x" } ] },
    { tag: "DEVICES", hv: false, title: "Devices + Disk: hardware and live activity",
      why: "Devices remembers every plugged or once-recognized device (virtual disks, USBs); Disk shows what churns now — recordings, background VPNs, anything alive mid-SS.",
      path: ["SYSTEM INFORMER", "DEVICES TAB", "DISK TAB"],
      steps: ["Open Devices; audit plugged plus historically recognized hardware.", "Challenge unknown virtual disks/USBs mounted near the freeze; reconcile USB history.", "Flip to Disk — live recordings or VPN sessions mid-SS demand an explanation."],
      reds: ["Mystery virtual disks/USBs near the freeze; unexplained recording/VPN mid-SS."],
      normal: "Familiar drives only, no background recording or VPN.",
      tip: "FAT32 keeps no USN journal — a cheat run purely off FAT32 barely touches disk, so Devices is the only net.",
      filters: [] },
    { tag: "X-CHECK", hv: false, title: "Cross-check matrix: two sources or it did not happen",
      why: "Single-source hits are suspects, not verdicts. A path confirmed in two independent stores — BAM registry, prefetch, DPS/CSRSS memory — is the unit of evidence this console is built around.",
      path: ["HIT LIST", "BAM × PREFETCH", "MEMORY"],
      steps: ["Collect every flagged path from the exhibit pages.", "Confirm each against at least one second store (BAM vs prefetch vs SI memory).", "Single-source hits stay open; dual-confirmed hits go in the verdict."],
      reds: ["Calling a ban on one unconfirmed string.", "Ignoring session timestamps when matching stores."],
      normal: "Every verdict line cites two stores plus a timestamp.",
      tip: "Work the matrix oldest-first so the story reads like a timeline, not a pile.",
      filters: [] },
    { tag: "DISCIPLINE", hv: false, title: "Session discipline: record, CMD, AV off",
      why: "Sloppy procedure voids good evidence. The session rules are few and absolute.",
      path: ["RECORDER", "CMD", "DEFENDER OFF"],
      steps: ["Record the whole session start to finish.", "Disable Defender and third-party AV before touching memory.", "Run commands in CMD, never raw PowerShell.", "Screenshot full lines — process, PID, range, timestamp."],
      reds: ["Unrecorded gaps, AV quarantining mid-read, PowerShell-only trails."],
      normal: "Full recording, AV off, CMD trail, archived screenshots.",
      tip: "If AV ate a file mid-session, note it in the log — missing is also data.",
      filters: [] },
    { tag: "APP-SVC", hv: false, title: "This app: services, tasks, leftovers",
      why: "The Services docket (01) runs the automated side of cards 02 and 10: service integrity, the new event IDs, scheduled tasks and leftover files — everything with verdicts and tiles.",
      path: ["DOCK 01", "RUN CHECK", "TASK SCAN", "LEFTOVERS"],
      steps: ["RUN CHECK fills drives, services, events, USB, antivirus and recycle cards.", "TASK SCAN lists every scheduled task with triggers and flags boot/logon oddities.", "LEFTOVERS sweeps temp, recent links, dumps, hosts and PS history.", "SESSION + DEVICE SIGNALS rows with HIT need a manual follow-up in SI."],
      reds: ["Skipped sections mean no admin rights — relaunch elevated."],
      normal: "All cards populated, verdict counts match the tiles.",
      tip: "Run this docket first on every box; it is the cheapest evidence per second.",
      filters: [] },
    { tag: "APP-TRACE", hv: false, title: "This app: prefetch, amcache, bam, usn",
      why: "The traces dockets (02, 03, 07) answer one question from four stores: what executed. Prefetch and Amcache cover files, BAM the registry ledger, USN the live journal.",
      path: ["DOCK 02 / 03 / 07", "RUN", "FILTER", "ROW CLICK"],
      steps: ["Run all three scans, then combine signature chips to isolate unsigned and rule-hit rows.", "Sort newest-first; session rows carry the SESSION tag.", "Click any row for the evidence modal (related files, rule samples, tamper flags).", "Cross-check every hit across at least two stores before calling it."],
      reds: ["Unsigned + rule hit in two stores is strong; single-store hits stay suspects."],
      normal: "Signed system files, no rule hits, stable timelines.",
      tip: "The USN GONE flag is the fastest deleted-file lead in the whole app.",
      filters: [] },
    { tag: "APP-ID", hv: false, title: "This app: alts, mods, machine identity",
      why: "The Alts docket (04) links accounts to hardware: Minecraft names, Discord IDs, installed mods and the machine GUID plus disk serials for cross-PC linking.",
      path: ["DOCK 04", "RUN SCAN", "MACHINE IDENTITY"],
      steps: ["RUN SCAN collects launchers, logs, Discord clients and browsers.", "More than one Minecraft account is itself a flag — check the tile.", "MACHINE IDENTITY gives the GUID and disk serials: reuse them to link this box to other cases.", "The MODS card lists every installed jar for the obfuscation pass."],
      reds: ["Multiple accounts, unknown Discord IDs, jars you cannot attribute."],
      normal: "One account, known IDs, attributable mods.",
      tip: "Screenshot the identity card per case; GUIDs are the join key across sessions.",
      filters: [] },
    { tag: "APP-TIME", hv: false, title: "This app: fused timeline and settings",
      why: "The Timeline docket (09) merges every timestamped finding into one newest-first story. Settings on the same page control the case directory, feeds, session filter and row cap.",
      path: ["DOCK 09", "SETTINGS", "REFRESH"],
      steps: ["Run at least two scans, then open Timeline and press REFRESH.", "Uncheck feeds you do not need; SESSION ONLY cuts pre-boot noise.", "Set the case directory once — reports, snapshots and exports land there.", "Read top-down: the BOOT row anchors the session."],
      reds: ["An empty timeline means no scans ran yet — not a clean box."],
      normal: "Dense around boot and freeze time, quiet elsewhere.",
      tip: "SESSION ONLY plus all feeds on is the default triage view.",
      filters: [] },
    { tag: "APP-CASE", hv: false, title: "This app: full case scan and html report",
      why: "The overview CASE SCAN runs all eight modules in order with one bar, saves a snapshot for diffing, hashes flagged files and writes the standalone HTML report.",
      path: ["OVERVIEW", "RUN FULL CASE SCAN", "SAVE + OPEN FULL REPORT"],
      steps: ["Press RUN FULL CASE SCAN once — progress is a single bar by design.", "When COMPLETE lands, check TOTAL and the Δ line against the previous scan.", "SAVE + OPEN FULL REPORT writes the file into the case directory and opens it.", "The report carries verdict, risk, detections, hashes, screenshot and the full module detail."],
      reds: ["FAIL rows mean that module needs a manual solo run."],
      normal: "8 / 8 done, report saved, no fails.",
      tip: "Run the case scan twice per box: once at arrival, once before leaving — the Δ is the story.",
      filters: [] }
  ];
  function hiRe(s) {
    return esc(s).replace(/([\\^$.*+?()[\]{}|])/g, "<b>$1</b>");
  }
  function renderGuide(filter) {
    var q = (filter || "").toLowerCase();
    var list = $("guideList");
    list.innerHTML = "";
    var shown = 0;
    GUIDE.forEach(function (card, idx) {
      var hay = (card.tag + " " + card.title + " " + card.why + " " +
        card.filters.map(function (f) { return f.label + " " + f.pattern; }).join(" ")).toLowerCase();
      if (q && hay.indexOf(q) < 0) return;
      shown++;
      var el = document.createElement("div");
      el.className = "gcard";
      var fhtml = card.filters.length
        ? '<div class="gsec"><b>FILTERS · ' + card.filters.length + "</b>" + card.filters.map(function (f) {
          var code = f.type === "Regex" ? hiRe(f.pattern) : esc(f.pattern);
          return '<div class="flt"><span class="ftype">' + esc(f.type) + "</span>" +
            '<span class="fname">' + esc(f.label) + "</span>" +
            '<code class="flcode">' + code + "</code>" +
            '<button data-copy="' + esc(f.pattern) + '">COPY</button></div>';
        }).join("") + "</div>"
        : '<div class="gsec"><b>FILTERS · 0</b><div class="meta">NO FILTER — SETUP/CONTEXT CARD.</div></div>';
      el.innerHTML =
        '<button class="ghead"><span class="gnum">' + (idx + 1 < 10 ? "0" : "") + (idx + 1) + "</span>" +
        '<span class="gtag">' + esc(card.tag) + "</span>" +
        (card.hv ? '<span class="hv">HIGH VALUE</span>' : "") +
        "<span>" + esc(card.title.toUpperCase()) + "</span>" +
        '<span class="gchev">▾</span></button>' +
        '<div class="gbody">' +
        '<div class="gsec"><b>WHY IT MATTERS</b><p>' + esc(card.why) + "</p></div>" +
        '<div class="gsec"><b>CLICK PATH</b><div class="cpath">' +
        card.path.map(function (s) { return "<span>" + esc(s) + "</span>"; }).join("<i>→</i>") +
        "</div></div>" +
        '<div class="gsec"><b>STEPS</b><ol>' +
        card.steps.map(function (s) { return "<li>" + esc(s) + "</li>"; }).join("") +
        "</ol></div>" +
        (card.warn ? '<div class="warnbox">! ' + esc(card.warn) + "</div>" : "") +
        '<div class="flagrow red"><b>RED FLAGS</b><span>' + card.reds.map(esc).join("<br>") + "</span></div>" +
        '<div class="flagrow nrm"><b>NORMAL</b><span>' + esc(card.normal) + "</span></div>" +
        '<div class="flagrow tip"><b>ANALYST TIP</b><span>' + esc(card.tip) + "</span></div>" +
        fhtml + "</div>";
      el.querySelector(".ghead").addEventListener("click", function () { el.classList.toggle("open"); Snd.tick(); });
      list.appendChild(el);
    });
    $("gCount").textContent = shown + " / " + GUIDE.length;
    list.querySelectorAll("[data-copy]").forEach(function (b) {
      b.addEventListener("click", function (ev) {
        ev.stopPropagation();
        copyText(b.getAttribute("data-copy"));
      });
    });
  }
  function copyText(v) {
    function done() { toast("COPIED TO CLIPBOARD"); Snd.ok(); }
    if (navigator.clipboard && navigator.clipboard.writeText) {
      navigator.clipboard.writeText(v).then(done, function () { fallback(); });
    } else fallback();
    function fallback() {
      var ta = document.createElement("textarea");
      ta.value = v;
      document.body.appendChild(ta);
      ta.select();
      try { document.execCommand("copy"); done(); } catch (e) { toast("COPY FAILED"); }
      document.body.removeChild(ta);
    }
  }
  $("gSearch").addEventListener("input", function () { renderGuide($("gSearch").value); });
  $("gExpand").addEventListener("click", function () {
    document.querySelectorAll(".gcard").forEach(function (c) { c.classList.add("open"); });
  });
  $("gCollapse").addEventListener("click", function () {
    document.querySelectorAll(".gcard").forEach(function (c) { c.classList.remove("open"); });
  });
  renderGuide("");
})();
