const fs = require("fs");
const src = fs.readFileSync("src/MaximTool/Assets/script.js", "utf8");

function anyEl() {
  return new Proxy(function () {}, {
    get(t, p) {
      if (p === "querySelectorAll") return () => [];
      if (p === "querySelector") return () => anyEl();
      if (p === "dataset") return {};
      if (p === "style") return {};
      if (p === "classList") return { add() {}, remove() {}, toggle() {}, contains() { return false; } };
      if (p === "textContent" || p === "innerHTML" || p === "value") return "";
      if (p === Symbol.toPrimitive) return () => "";
      return (...a) => anyEl();
    },
    set() { return true; },
    apply() { return anyEl(); },
  });
}

const handlers = {};
const recorders = {};
function elFor(id) {
  if (!recorders[id]) {
    const el = {
      _html: "", _text: "", _dis: false, _chk: false,
      classList: { add() {}, remove() {}, toggle() {}, contains() { return false; } },
      style: {}, dataset: {}, scrollTop: 0,
      querySelectorAll(sel) {
        if (id === "chkList" && sel === "[data-chk]") {
          return (this._chks || []).map((cid) => ({
            dataset: { chk: cid },
            get checked() { return recorders._chkState ? recorders._chkState[cid] : false; },
            set checked(v) {},
            addEventListener(ev, fn) { handlers["chk:" + cid + ":" + ev] = fn; },
          }));
        }
        if (id === "mcProfList" && sel === "[data-mcdel]") {
          return (this._dels || []).map((p) => ({
            dataset: { mcdel: p },
            addEventListener(ev, fn) { handlers["mcdel:" + ev] = fn; },
          }));
        }
        return [];
      },
      querySelector() { return anyEl(); },
      appendChild() {}, remove() {},
      addEventListener(ev, fn) { handlers[id + ":" + ev] = fn; },
    };
    Object.defineProperty(el, "innerHTML", { get() { return this._html; }, set(v) { this._html = String(v); } });
    Object.defineProperty(el, "textContent", { get() { return this._text; }, set(v) { this._text = String(v); } });
    Object.defineProperty(el, "value", { get() { return ""; }, set(v) {} });
    Object.defineProperty(el, "checked", { get() { return this._chk; }, set(v) { this._chk = !!v; } });
    Object.defineProperty(el, "disabled", { get() { return this._dis; }, set(v) { this._dis = !!v; } });
    recorders[id] = el;
  }
  return recorders[id];
}

const mcProfiles = [
  { path: "C:\\Users\\x\\AppData\\Roaming\\.minecraft", kind: "VANILLA", source: "AUTO", lastUsed: "2026-10-17 01:00:00", exists: true },
  { path: "D:\\Games\\MC\\.minecraft", kind: "VANILLA", source: "MANUAL", lastUsed: "2026-10-16 01:00:00", exists: true },
];
let cfgSaved = null;
let altRan = false;

global.window = {
  matchMedia: () => ({ matches: true }),
  addEventListener() {},
  pywebview: { api: {
    mc_profiles: () => Promise.resolve(mcProfiles.map((p) => ({ ...p }))),
    mc_profile_add: (p) => {
      mcProfiles.push({ path: p, kind: "VANILLA", source: "MANUAL", lastUsed: "", exists: true });
      return Promise.resolve({ ok: true, path: p });
    },
    mc_profile_remove: (p) => {
      const i = mcProfiles.findIndex((x) => x.path === p);
      if (i >= 0) mcProfiles.splice(i, 1);
      return Promise.resolve({ ok: true });
    },
    config_browse: () => Promise.resolve("E:\\MC\\.minecraft"),
    config_get: () => Promise.resolve(""),
    config_set: (patch) => { cfgSaved = patch; return Promise.resolve('{"ok":true}'); },
    alt_detector_run: () => { altRan = true; return Promise.resolve({
      minecraftAccounts: ["a"], discordIds: [], discordAccounts: [],
      launcherFilesScanned: 1, logFilesScanned: 0, hwid: { machineGuid: "G", disks: [] },
      mods: [], profiles: mcProfiles,
    }); },
    alt_detector_clear: () => Promise.resolve(true),
    usn_drives: () => Promise.resolve([]),
    everything_status: () => Promise.reject(new Error("x")),
    system_informer_status: () => Promise.reject(new Error("x")),
    service_checker_run: () => Promise.resolve({ admin: true, findingCount: 0, boot: {}, drives: [], services: [], events: { skipped: true }, recycle: { skipped: true } }),
  } },
};
global.document = {
  documentElement: { setAttribute() {}, getAttribute: () => "dark" },
  getElementById: (id) => elFor(id),
  querySelectorAll: () => [],
  createElement: () => anyEl(),
  addEventListener() {},
  activeElement: { tagName: "BODY" },
};
global.localStorage = { getItem: () => null, setItem() {} };
global.navigator = {};
global.matchMedia = () => ({ matches: true });
global.requestAnimationFrame = (cb) => cb(16);
global.setInterval = () => 0;
global.setTimeout = () => 0;

eval(src);

(async () => {
  for (let i = 0; i < 10; i++) await new Promise((r) => setImmediate(r));
  const mcHtml = recorders["mcProfList"]._html;
  const mcMsg = recorders["mcMsg"]._text;

  // alts view
  await handlers["runAlts:click"]();
  for (let i = 0; i < 5; i++) await new Promise((r) => setImmediate(r));
  const profHtml = recorders["altsProfList"]._html;

  // checklist: simulate toggle
  recorders._chkState = {};
  const chkRowHtml = recorders["chkList"]._html;
  if (handlers["chk:svc-run:change"]) {
    handlers["chk:svc-run:change"](); // checked getter reads _chkState -> false
  }

  // add flow
  handlers["mcAdd:click"]();
  for (let i = 0; i < 5; i++) await new Promise((r) => setImmediate(r));

  const checks = [
    ["settings lists 2 profiles", mcHtml.includes("C:\\Users\\x\\AppData\\Roaming\\.minecraft") && mcHtml.includes("D:\\Games\\MC\\.minecraft")],
    ["settings badges", mcHtml.includes("VANILLA · AUTO") && mcHtml.includes("VANILLA · MANUAL")],
    ["remove button only on manual", (mcHtml.match(/data-mcdel/g) || []).length === 1],
    ["multi-recent warning in msg", mcMsg.includes("2 RECENTLY USED")],
    ["alts view shows profiles", profHtml.includes("D:\\Games\\MC\\.minecraft") && profHtml.includes("USED 2026-10-16")],
    ["checklist renders (no label wrap)", chkRowHtml.includes('class="chkrow"') && !chkRowHtml.includes("<label")],
    ["mc add calls browse then add", recorders["mcProfList"]._html.includes("E:\\MC\\.minecraft")],
    ["no syntax errors", true],
  ];
  let fail = 0;
  for (const [name, ok] of checks) {
    console.log((ok ? "PASS " : "FAIL ") + name);
    if (!ok) fail++;
  }
  process.exit(fail ? 1 : 0);
})();
