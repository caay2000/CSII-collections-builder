import React, { useEffect, useState } from "react";
import { createRoot } from "react-dom/client";
import { GameTopLeftButton } from "../src/panel";
import { actions, useBuilder, usePlan, useToolActive } from "../src/bindings";
import { areaPicked, addToCurrent, CATALOG, note, state, subscribe } from "./backend";
import { useSyncExternalStore } from "react";
import { GAME_ICONS } from "./game-icons";

const ICON = "data:image/svg+xml;utf8," + encodeURIComponent(
    '<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" fill="none" stroke="#ffffff" stroke-width="1.7" stroke-linejoin="round" stroke-linecap="round"><path d="M8 21V6M11 21V6M8 9l3 3M8 14l3 3M5 21h9"/><path d="M2 6h20M9.5 2l-4.5 4M9.5 2l9 4"/><rect x="3" y="7" width="4" height="3"/><path d="M19 6v7"/><path d="M17.5 13h3v2.5h-3z"/></svg>');

function Scene() {
    const tool = useToolActive();
    const b = useBuilder();
    const plan = usePlan();
    const [pts, setPts] = useState<[number, number][]>([]);
    const [seed, setSeed] = useState(0);
    useEffect(() => { if (!tool) setPts([]); }, [tool]);
    useEffect(() => { if (!plan.areaSelected) setPts([]); }, [plan.areaSelected]);
    useSyncExternalStore(subscribe, () => state.seed);
    const blocks = [[60, 60], [60, 330], [380, 60], [380, 330], [700, 60], [700, 330]];
    const [sel, setSel] = useState(-1);
    useEffect(() => { if (!plan.areaSelected) setSel(-1); }, [plan.areaSelected]);
    const click = (e: React.MouseEvent<SVGSVGElement>) => {
        if (!tool) return;
        const r = e.currentTarget.getBoundingClientRect();
        const x = ((e.clientX - r.left) / r.width) * 1000, y = ((e.clientY - r.top) / r.height) * 600;
        if (b.mode === "block") {
            const i = blocks.findIndex(([bx, by]) => x >= bx && x <= bx + 260 && y >= by && y <= by + 230);
            if (i >= 0) { setSel(i); areaPicked(Math.round(18 * (0.6 + 0.4 * (b.density / 100)))); note("Bloque " + (i + 1) + " seleccionado"); }
        } else {
            if (pts.length >= 3 && Math.hypot(x - pts[0][0], y - pts[0][1]) < 20) { areaPicked(Math.round(14 * (0.6 + 0.4 * (b.density / 100)))); note("Polígono cerrado"); return; }
            if (!plan.areaSelected) setPts([...pts, [x, y]]);
        }
    };
    const bi = sel >= 0 ? blocks[sel] : null;
    const n = plan.previewing ? plan.lots : 0;
    return (
        <svg viewBox="0 0 1000 600" preserveAspectRatio="xMidYMid slice" className="scene" onClick={click} onContextMenu={(e) => { e.preventDefault(); setPts([]); }}>
            <rect width="1000" height="600" fill="#3a4d2c" />
            <rect y="290" width="1000" height="30" fill="#59616b" /><rect x="320" width="30" height="600" fill="#59616b" />
            <rect x="640" width="30" height="600" fill="#59616b" />
            {blocks.map(([x, y], i) => <rect key={i} x={x} y={y} width="260" height="230" fill={tool ? "#7f8a96" : "#46603a"} stroke="#2b3a24" strokeWidth="2" opacity={tool ? 0.85 : 1} />)}
            {tool && blocks.map(([x, y], i) => <g key={"g" + i}>{Array.from({ length: 6 }).map((_, r) => Array.from({ length: 8 }).map((__, c) =>
                <rect key={r + "-" + c} x={x + 4 + c * 32} y={y + 4 + r * 37} width="28" height="33" fill="#9aa6b2" opacity=".5" />))}</g>)}
            {bi && <rect x={bi[0]} y={bi[1]} width="260" height="230" fill="#facc1533" stroke="#facc15" strokeWidth="4" />}
            {b.mode === "polygon" && pts.length > 0 && <polyline points={pts.map((p) => p.join(",")).join(" ") + (plan.areaSelected ? " " + pts[0].join(",") : "")} fill={plan.areaSelected ? "#facc1533" : "none"} stroke="#facc15" strokeWidth="4" strokeDasharray="10 6" />}
            {b.mode === "polygon" && pts.map((p, i) => <circle key={i} cx={p[0]} cy={p[1]} r={i === 0 ? 10 : 6} fill="#facc15" />)}
            {Array.from({ length: n }).map((_, i) => {
                const base = bi ?? [pts.length ? Math.min(...pts.map((p) => p[0])) : 100, pts.length ? Math.min(...pts.map((p) => p[1])) : 100];
                const x = base[0] + 12 + (i % 6) * 40 + ((i * 7 + state.seed * 13) % 5), y = base[1] + 20 + Math.floor(i / 6) * 55 + ((i * 3 + state.seed * 7) % 6);
                return <rect key={i} x={x} y={y} width="34" height="44" rx="3" fill={["#60a5fa", "#7dd3fc", "#93c5fd"][(i + state.seed) % 3]} opacity=".9" />;
            })}
        </svg>
    );
}

function FindIt() {
    const [last, setLast] = useState("Shift+click a building to add it to the selected collection.");
    useSyncExternalStore(subscribe, () => JSON.stringify(state.cols) + state.status);
    return (
        <div className="findit">
            <div className="fiHead">Find It (simulated)</div>
            <div className="fiGrid">
                {CATALOG.slice(0, 12).map((b) => (
                    <div key={b.id} className="fiCard" onClick={(e) => {
                        if (e.shiftKey) { addToCurrent(b.id); setLast("Added " + b.name); } else setLast("Plain click: no change (Shift+click adds).");
                    }}><img src={b.icon} /><span>{b.name}</span></div>
                ))}
            </div>
            <div className="fiNote">{last}</div>
        </div>
    );
}

function Harness() {
    const [zoom, setZoom] = useState(1.2);
    useSyncExternalStore(subscribe, () => state.log.join("|") + state.status);
    useEffect(() => { document.documentElement.style.setProperty("--u", zoom + "px"); }, [zoom]);
    return (
        <div className="stage">
            <Scene />
            <div className="topbar">
                {[0, 1, 2].map((i) => <div key={i} className="gameBtn dummy" />)}
                <GameTopLeftButton />
            </div>
            <div className="zoom">Zoom {[1, 1.2, 1.6, 2].map((z) => <button key={z} className={z === zoom ? "on" : ""} onClick={() => setZoom(z)}>{z}x</button>)}</div>
            <FindIt />
            <div className="log">{state.log.map((l, i) => <div key={i}>{l}</div>)}</div>
        </div>
    );
}

// El icono real sale de coui://; en el navegador lo sustituimos por un data URI.
const origCreate = document.createElement.bind(document);
document.createElement = ((tag: string, o?: any) => origCreate(tag, o)) as any;
const ICONS: Record<string, string> = (window as any).BC_ICONS ?? {};
const mo = new MutationObserver(() => document.querySelectorAll<HTMLImageElement>('img[src^="coui://"], img[src^="Media/"]').forEach((i) => {
    const src = i.getAttribute("src") || "";
    const m = /icons\/([\w-]+)\.svg$/.exec(src);
    const g = /([\w]+)\.svg$/.exec(src);
    i.src = (m && ICONS[m[1]]) || (g && GAME_ICONS[g[1]]) || ICON;
}));
mo.observe(document.documentElement, { childList: true, subtree: true, attributes: true, attributeFilter: ["src"] });
createRoot(document.getElementById("root")!).render(<Harness />);

// #builder / #tab=builder en la URL: abre esa pestaña (para capturas automáticas).
setTimeout(() => {
    const want = location.hash.replace("#", "");
    if (want === "builder") [...document.querySelectorAll("button")].find((b) => b.textContent === "Builder")?.click();
}, 100);

setTimeout(() => {
    if (location.hash !== "#builder-demo") return;
    const btn = (t: string) => [...document.querySelectorAll("button")].find((b) => b.textContent === t) as HTMLButtonElement | undefined;
    btn("Builder")?.click();
    setTimeout(() => {
        const svg = document.querySelector("svg.scene") as SVGSVGElement;
        const r = svg.getBoundingClientRect();
        const cx = r.left + r.width * 0.76, cy = r.top + r.height * 0.3;
        svg.dispatchEvent(new MouseEvent("click", { bubbles: true, clientX: cx, clientY: cy }));
        setTimeout(() => btn("Generate preview")?.click(), 60);
    }, 150);
}, 200);

setTimeout(() => {
    if (location.hash !== "#builder-picker") return;
    const btn = (t: string) => [...document.querySelectorAll("button")].find((b) => b.textContent === t) as HTMLButtonElement | undefined;
    btn("Builder")?.click();
    setTimeout(() => ([...document.querySelectorAll("button")].find((b) => /\(\d+\)/.test(b.textContent || "")) as HTMLButtonElement)?.click(), 80);
}, 200);

setTimeout(() => {
    if (location.hash !== "#builder-mix") return;
    const btn = (t: string) => [...document.querySelectorAll("button")].find((b) => b.textContent === t) as HTMLButtonElement | undefined;
    btn("Builder")?.click();
    setTimeout(() => btn("Add another collection")?.click(), 80);
}, 200);

setTimeout(() => {
    if (location.hash !== "#builder-trees") return;
    const btn = (t: string) => [...document.querySelectorAll("button")].find((b) => b.textContent === t) as HTMLButtonElement | undefined;
    btn("Builder")?.click();
    setTimeout(() => {
        const row = [...document.querySelectorAll("span")].find((x) => x.textContent === "Trees in leftover space");
        (row?.parentElement?.querySelector("button") as HTMLButtonElement)?.click();
        setTimeout(() => document.querySelector("[class*=builderFilters]:nth-of-type(3)")?.scrollIntoView(), 80);
    }, 80);
}, 200);
