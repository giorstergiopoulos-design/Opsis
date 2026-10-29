// Wallpaper elements: the pieces of the Opsis widgets (clock, CPU, memory, GPU, network, disks, battery, audio spectrum) drawn directly on the
// wallpaper, on top of any wallpaper mode. The user chooses which ones are shown and where (Wallpaper page -> "Wallpaper elements").
// Loaded by index.html; uses its globals: cfg, isPaused, bridge().
(function () {
    const root = document.createElement('div');
    root.id = 'elements';
    document.addEventListener('DOMContentLoaded', () => document.body.appendChild(root));
    if (document.body) document.body.appendChild(root);

    const css = document.createElement('style');
    css.textContent = `
    #elements{position:fixed;top:0;bottom:0;display:flex;flex-direction:column;flex-wrap:wrap;gap:14px;padding:3vh 2vw;box-sizing:border-box;pointer-events:none;z-index:5;overflow:hidden}
    #elements.left{left:0;align-items:flex-start;align-content:flex-start}
    #elements.right{right:0;align-items:flex-end;align-content:flex-start;flex-wrap:wrap-reverse}
    .el{position:relative;zoom:var(--s,1);flex:none;color:#fff;font-family:"Segoe UI",system-ui,sans-serif;
        padding:14px 18px;border-radius:16px;width:190px;box-sizing:border-box;--a:#00d2ff;text-shadow:0 1px 6px rgba(0,0,0,.55)}
    .elCard .el, .el.card{background:rgba(10,14,24,.42);backdrop-filter:blur(14px);-webkit-backdrop-filter:blur(14px);border:1px solid rgba(255,255,255,.12);box-shadow:0 8px 28px rgba(0,0,0,.25)}
    .elT{font-size:12px;letter-spacing:.14em;text-transform:uppercase;opacity:.75;margin-bottom:6px}
    .elBig{font-size:30px;font-weight:300;font-variant-numeric:tabular-nums;line-height:1.1}
    .elSub{font-size:12px;opacity:.7;margin-top:4px}
    .elBar{height:6px;border-radius:4px;background:rgba(255,255,255,.16);overflow:hidden;margin-top:8px}
    .elBar>i{display:block;height:100%;width:0;background:var(--a);transition:width .8s ease}
    .elRow{display:flex;justify-content:space-between;gap:16px;font-size:13px;margin-top:6px;font-variant-numeric:tabular-nums}
    .elBar>i{background:var(--b,var(--a))}
    #elements.center{left:0;right:0;align-items:center;align-content:center;justify-content:center}
    .elGpu+.elGpu{margin-top:10px}
    .el svg.elClock{display:block;width:100%;height:auto;margin:0 auto}
    .elDisk{margin-top:9px;font-size:12px}.elDisk b{font-weight:600}
    canvas.elCv{display:block}
    `;
    document.head.appendChild(css);

    const $q = (e, k) => e.querySelector('[data-k="' + k + '"]');
    const setText = (e, k, t) => { const n = $q(e, k); if (n && n.textContent !== t) n.textContent = t; };
    const setBar = (e, k, p) => { const n = $q(e, k); if (n) n.style.width = Math.max(0, Math.min(100, p)) + '%'; };
    const fmtRate = k => k >= 1024 ? (k / 1024).toFixed(1) + ' MB/s' : k.toFixed(0) + ' KB/s';
    const fmtGb = mb => (mb / 1024).toFixed(mb >= 10240 ? 0 : 1) + ' GB';

    // Clock themes (same names as the desktop clock widget): which ticks are drawn and how thick/coloured the hands are.
    function clockTicks(e, theme) {
        const g = $q(e, 'ticks'); let s = '';
        const minimal = theme === 'Minimal', retro = theme === 'Retro', mono = theme === 'Mono', neon = theme === 'Neon';
        for (let i = 0; i < 12; i++) { if (minimal && i % 3) continue; s += '<line y1="-84" y2="' + (i % 3 ? -76 : -70) + '" stroke="' + (retro ? 'var(--a)' : '#fff') + '" stroke-width="' + ((i % 3 ? 2 : 3) * (retro ? 1.5 : 1)) + '" transform="rotate(' + i * 30 + ')"/>'; }
        g.innerHTML = s;
        const hand = (k, w) => { const n = $q(e, k); n.setAttribute('stroke-width', w); n.setAttribute('stroke', neon || retro ? 'var(--a)' : '#fff'); n.style.filter = neon ? 'drop-shadow(0 0 4px var(--a))' : ''; };
        const wScale = minimal ? .7 : retro ? 1.3 : 1;
        hand('h', 6 * wScale); hand('m', 4 * wScale);
        const sec = $q(e, 's'); sec.style.display = minimal ? 'none' : ''; sec.setAttribute('stroke', mono ? 'rgba(255,255,255,.6)' : 'var(--a)');
    }

    const defs = {
        analog: {
            html: '<svg class="elClock" data-k="svg" viewBox="-100 -100 200 200"><circle r="92" fill="none" stroke="rgba(255,255,255,.35)" stroke-width="2"/>' +
                  '<g data-k="ticks"></g><line data-k="h" y2="-48" stroke="#fff" stroke-width="6" stroke-linecap="round"/><line data-k="m" y2="-70" stroke="#fff" stroke-width="4" stroke-linecap="round"/>' +
                  '<line data-k="s" y2="-78" stroke="var(--a)" stroke-width="2" stroke-linecap="round"/><circle r="5" fill="var(--a)"/></svg>',
            init(e) { clockTicks(e, 'Classic'); },
            theme(e, t) { if (e.dataset.ct === t) return; e.dataset.ct = t; clockTicks(e, t); },
            tick(e) { const d = new Date(), s = d.getSeconds() + d.getMilliseconds() / 1000, m = d.getMinutes() + s / 60, h = (d.getHours() % 12) + m / 60;
                $q(e, 'h').setAttribute('transform', 'rotate(' + h * 30 + ')'); $q(e, 'm').setAttribute('transform', 'rotate(' + m * 6 + ')'); $q(e, 's').setAttribute('transform', 'rotate(' + s * 6 + ')'); }
        },
        cpu: {
            html: '<div class="elT">CPU</div><div class="elBig" data-k="v">–</div><div class="elBar"><i data-k="b"></i></div>',
            upd(e, d) { setText(e, 'v', d.cpu.toFixed(0) + ' %'); setBar(e, 'b', d.cpu); }
        },
        ram: {
            html: '<div class="elT">RAM</div><div class="elBig" data-k="v">–</div><div class="elBar"><i data-k="b"></i></div><div class="elSub" data-k="s"></div>',
            upd(e, d) { setText(e, 'v', d.ramUsedPercent.toFixed(0) + ' %'); setBar(e, 'b', d.ramUsedPercent); setText(e, 's', fmtGb(d.ramTotalMb - d.ramAvailableMb) + ' / ' + fmtGb(d.ramTotalMb)); }
        },
        gpu: {
            needsGpu: true,
            html: '<div data-k="list"></div>',
            // EVERY graphics adapter (dedicated and integrated) is listed, one block each - the wallpaper cannot be clicked, so there are no arrows to flip through them
            upd(e, d) { const list = (d.gpu && d.gpu.all && d.gpu.all.length) ? d.gpu.all : (d.gpu ? [d.gpu] : []); const box = $q(e, 'list');
                if (!list.length) { box.textContent = 'n/a'; return; }
                const sig = list.length; if (box.dataset.n != sig) { box.dataset.n = sig; box.innerHTML = list.map(() => '<div class="elGpu"><div class="elT" data-k="n">GPU</div><div class="elBig" data-k="v">–</div><div class="elBar"><i data-k="b"></i></div><div class="elSub" data-k="s"></div></div>').join(''); }
                [...box.children].forEach((c, i) => { const g = list[i]; const q = k => c.querySelector('[data-k="' + k + '"]'); const set = (k, t) => { const n = q(k); if (n && n.textContent !== t) n.textContent = t; };
                    set('n', (g.name || 'GPU').slice(0, 28)); if (g.load == null) { set('v', 'n/a'); return; }
                    set('v', g.load.toFixed(0) + ' %'); q('b').style.width = Math.max(0, Math.min(100, g.load)) + '%'; set('s', (g.temperature != null ? g.temperature.toFixed(0) + ' °C' : '') + (g.memoryUsedMb != null ? '  ·  ' + fmtGb(g.memoryUsedMb) : '')); }); }
        },
        network: {
            html: '<div class="elT">Network</div><div class="elRow"><span>↓</span><b data-k="d">–</b></div><div class="elRow"><span>↑</span><b data-k="u">–</b></div>',
            upd(e, d) { setText(e, 'd', fmtRate(d.netDownKbps)); setText(e, 'u', fmtRate(d.netUpKbps)); }
        },
        disks: {
            html: '<div class="elT">Disks</div><div data-k="list"></div>',
            upd(e, d) { const l = $q(e, 'list'); const items = d.disks || []; const sig = JSON.stringify(items.map(x => [x.name, Math.round(x.usedPercent)]));
                if (l.dataset.sig === sig) return; l.dataset.sig = sig;
                l.innerHTML = items.map(x => '<div class="elDisk"><div class="elRow" style="margin:0"><b>' + x.name + '</b><span>' + fmtGb(x.freeMb) + ' free</span></div><div class="elBar"><i style="width:' + x.usedPercent + '%"></i></div></div>').join(''); }
        },
        battery: {
            html: '<div class="elT">Battery</div><div class="elBig" data-k="v">–</div><div class="elBar"><i data-k="b"></i></div>',
            upd(e, d) { if (d.batteryPercent < 0) { setText(e, 'v', 'AC'); setBar(e, 'b', 100); return; } setText(e, 'v', d.batteryPercent + ' %' + (d.charging ? ' ⚡' : '')); setBar(e, 'b', d.batteryPercent); }
        },
        audio: {
            needsAudio: true,
            html: '<div class="elT">Audio</div><canvas class="elCv" data-k="cv" width="260" height="90" style="width:100%;height:auto"></canvas>',
            draw(e, bands) { const cv = $q(e, 'cv'), c = cv.getContext('2d'); c.clearRect(0, 0, cv.width, cv.height);
                const n = bands.length || 24, bw = cv.width / n; c.fillStyle = getComputedStyle(e).getPropertyValue('--a') || '#00d2ff';
                for (let i = 0; i < n; i++) { const v = Math.min(1, (bands[i] || 0) * 1.2), h = Math.max(2, v * cv.height); c.globalAlpha = .4 + .6 * v; c.fillRect(i * bw + 1.5, cv.height - h, bw - 3, h); } c.globalAlpha = 1; }
        },
    };

    let active = new Map();      // id -> element
    let timer = null, audioTimer = null, clockTimer = null;
    const needs = k => [...active.keys()].some(id => defs[id] && defs[id][k]);

    function clearTimers() { clearInterval(timer); clearInterval(audioTimer); clearInterval(clockTimer); timer = audioTimer = clockTimer = null; }

    async function pollSystem() {
        if (isPaused) return;
        const b = bridge(); if (!b) return;
        try {
            const d = JSON.parse(await b.GetSystemJson(needs('needsGpu')));
            for (const [id, e] of active) if (defs[id].upd) defs[id].upd(e, d);
        } catch (x) { /* keep the last values */ }
    }
    async function pollAudio() {
        if (isPaused) return;
        const b = bridge(); if (!b) return;
        try { const bands = JSON.parse(await b.GetAudioBandsJson()); for (const [id, e] of active) if (defs[id].draw) defs[id].draw(e, bands); } catch (x) { /* ignore */ }
    }
    function tickClocks() { if (isPaused) return; for (const [id, e] of active) if (defs[id].tick) defs[id].tick(e); }

    // Called after every settings refresh: builds exactly the elements the user enabled, where and as big as they asked.
    window.opsisApplyElements = async function () {
        const b = bridge(); if (!b) return;
        let c = cfg;
        try { c = JSON.parse(await b.GetWaveConfigJson()); } catch (x) { /* use the last one */ }
        const list = (c.elements || []).filter(x => defs[x.id]);
        const want = new Map(list.map(x => [x.id, x]));
        for (const [id, e] of [...active]) if (!want.has(id)) { e.remove(); active.delete(id); }
        root.className = c.elSide === 'Right' ? 'right' : c.elSide === 'Center' ? 'center' : 'left';
        root.style.justifyContent = c.elVAlign === 'Top' ? 'flex-start' : c.elVAlign === 'Bottom' ? 'flex-end' : 'center';
        for (const x of list) {
            let e = active.get(x.id);
            if (!e) {
                e = document.createElement('div'); e.className = 'el'; e.innerHTML = defs[x.id].html; root.appendChild(e); active.set(x.id, e);
                if (defs[x.id].init) defs[x.id].init(e);
            }
            root.appendChild(e);                       // keeps the catalog order (appending an existing node just moves it)
            e.style.setProperty('--s', (x.scale || 100) / 100); e.style.setProperty('--a', c.elAccent || '#00d2ff'); if (c.elBarColor) e.style.setProperty('--b', c.elBarColor); else e.style.removeProperty('--b');
            if (defs[x.id].theme) defs[x.id].theme(e, c.elClockTheme || 'Classic');
            e.classList.toggle('card', !!c.elCard);
        }
        clearTimers();
        if (active.size === 0) return;
        if ([...active.keys()].some(id => defs[id].upd)) { pollSystem(); timer = setInterval(pollSystem, 1000); }
        if (needs('needsAudio')) audioTimer = setInterval(pollAudio, 80);
        if ([...active.keys()].some(id => defs[id].tick)) { tickClocks(); clockTimer = setInterval(tickClocks, 1000); }
    };
    window.opsisPauseElements = function (paused) { if (paused) clearTimers(); else window.opsisApplyElements(); };
})();
