// Simulador del lado C# (BcUISystem) para ejecutar la UI real en un navegador. No es código del mod.
import type { BuildingDto, BuilderDto, CollectionDto, PlanDto } from "../src/bindings";
const DEFAULT_BUILDER: BuilderDto = { mode: "block", widthOp: 0, widthValue: 0, depthOp: 0, depthValue: 0, levelOp: 0, levelValue: 0, density: 100 };

type Listener = () => void;
const listeners = new Set<Listener>();
const emit = () => listeners.forEach((l) => l());
export const subscribe = (l: Listener) => { listeners.add(l); return () => { listeners.delete(l); }; };

function icon(name: string, level: number, w: number, d: number): string {
    const wall = ["#c9825b", "#d4a373", "#b4876a", "#9aa6b2", "#7d8fa3"][(level - 1) % 5];
    const h = 14 + level * 6 + d * 3, bw = 22 + w * 9;
    const wins = Array.from({ length: level + 1 }).map((_, i) =>
        `<rect x="${14 + (i % 3) * 11}" y="${46 - Math.floor(i / 3) * 11}" width="6" height="7" fill="#1b2433"/>`).join("");
    const svg = `<svg xmlns="http://www.w3.org/2000/svg" width="64" height="56" viewBox="0 0 64 56"><rect width="64" height="56" rx="4" fill="#26344a"/>` +
        `<rect x="9" y="${54 - h}" width="${bw}" height="${h}" fill="${wall}" rx="2"/><path d="M7 ${54 - h}l${bw / 2 + 2} -9 ${bw / 2 + 2} 9z" fill="#6b3f2e"/>${wins}</svg>`;
    return "data:image/svg+xml;utf8," + encodeURIComponent(svg);
}

const FAMILIES: [string, number, number, string, string][] = [
    ["BE Worker Row Housing V1 Left", 1, 2, "Row", "Belgium"], ["BE Worker Row Housing V1 Right", 1, 2, "Row", "Belgium"],
    ["BE Townhouse Corner", 2, 2, "Medium", "Belgium"], ["FR Neo Row Housing 01", 2, 2, "Row", "France"],
    ["FR Neo Row Housing 02", 2, 3, "Row", "France"], ["FR Neo Mid-Rise", 3, 3, "Medium", "France"],
    ["Cobblestone Apartments", 3, 2, "Medium", "Generic"], ["Old Town Mixed Use", 3, 3, "Mixed", "Generic"],
    ["Harbor Lofts", 4, 3, "High", "Generic"], ["Garden Villa", 2, 3, "Low", "Generic"],
];
const ALL: BuildingDto[] = [];
FAMILIES.forEach(([n, w, d, zone, pack]) => [1, 2, 3, 4, 5].forEach((lv) => {
    if ((n.length + lv) % 4 === 0) return;
    ALL.push({ id: `${n} L${lv}`, name: `${n} - L${lv} ${w}x${d}`, w, d, level: lv, icon: icon(n, lv, w, d) });
}));
export const CATALOG = ALL;

interface State {
    panelOpen: boolean; toolActive: boolean; findIt: boolean;
    cols: { name: string; ids: string[] }[]; selected: string; status: string;
    builder: BuilderDto; plan: PlanDto; seed: number; log: string[]; anarchy: boolean;
}
const S: State = {
    panelOpen: true, toolActive: false, findIt: true,
    cols: [
        { name: "Neo French", ids: ALL.filter((b) => b.name.startsWith("FR")).map((b) => b.id) },
        { name: "med modern city", ids: ALL.filter((b) => b.w >= 3).map((b) => b.id) },
        { name: "med historic", ids: ALL.filter((b) => b.name.startsWith("BE")).map((b) => b.id) },
        { name: "Test", ids: [ALL[0].id] },
        ...Array.from({ length: 12 }, (_, i) => ({ name: "Extra collection " + (i + 1), ids: ALL.slice(0, i + 2).map((b) => b.id) })),
    ],
    selected: "Neo French", status: "", builder: { ...DEFAULT_BUILDER },
    plan: { areaSelected: false, previewing: false, lots: 0 }, seed: 1, log: [], anarchy: true,
};
export const state = S;
const TREE_SETS = ["YYTC-wild-deciduous-trees", "YYTC-evergreen-trees", "YYTC-wild-bushes",
    ...[1, 2, 3, 4, 5].map((i) => "YYTC-custom-set-" + i)];
const cur = () => S.cols.find((c) => c.name === S.selected);
export const note = (t: string) => { S.log.unshift(t); S.log = S.log.slice(0, 6); emit(); };

export function read(key: string): string {
    switch (key) {
        case "panelOpen": return JSON.stringify(S.panelOpen);
        case "toolActive": return JSON.stringify(S.toolActive);
        case "findItAvailable": return JSON.stringify(S.findIt);
        case "selected": return S.selected;
        case "status": return S.status;
        case "collections": return JSON.stringify(S.cols.map((c): CollectionDto => ({ name: c.name, tag: c.name.replace(/\s+/g, "-"), count: c.ids.length })));
        case "buildings": return JSON.stringify((cur()?.ids ?? []).map((id) => ALL.find((b) => b.id === id)).filter(Boolean));
        case "mixBuildings": {
            const m = (S.builder as any).mix as string | undefined;
            const c = m && m !== S.selected ? S.cols.find((x) => x.name === m) : undefined;
            return JSON.stringify((c?.ids ?? []).map((id) => ALL.find((b) => b.id === id)).filter(Boolean));
        }
        case "builder": return JSON.stringify(S.builder);
        case "plan": return JSON.stringify({ ...S.plan, anarchyAvailable: true, anarchy: S.anarchy, treeSets: TREE_SETS });
    }
    return "";
}

export function addToCurrent(id: string) {
    const c = cur();
    if (!c) { S.status = "No hay colección seleccionada."; emit(); return; }
    if (c.ids.includes(id)) { S.status = "Ya estaba en la colección."; emit(); return; }
    c.ids.push(id); S.status = "OK"; emit();
}

export function trigger(name: string, ...args: any[]) {
    const c = cur();
    switch (name) {
        case "setPanelOpen": S.panelOpen = !!args[0]; break;
        case "select": S.selected = args[0]; break;
        case "create": {
            const n = String(args[0] ?? "").trim();
            if (!n) { S.status = "El nombre está vacío."; break; }
            if (S.cols.some((x) => x.name.toLowerCase() === n.toLowerCase())) { S.status = `Ya existe la colección '${n}'.`; break; }
            S.cols.push({ name: n, ids: [] }); S.selected = n; S.status = "OK"; break;
        }
        case "clear": if (c) { c.ids = []; S.status = "OK"; } break;
        case "delete": if (c) { S.cols = S.cols.filter((x) => x !== c); S.selected = S.cols[0]?.name ?? ""; S.status = "OK"; } break;
        case "removeBuildings": if (c) { const rm = new Set<string>(JSON.parse(args[0])); c.ids = c.ids.filter((i) => !rm.has(i)); S.status = "OK"; } break;
        case "addAll": if (c) { ALL.slice(0, 6).forEach((b) => { if (!c.ids.includes(b.id)) c.ids.push(b.id); }); S.status = "OK"; } break;
        case "setBuilder": S.builder = JSON.parse(args[0]); S.plan = { ...S.plan }; break;
        case "setToolActive": S.toolActive = !!args[0]; if (!S.toolActive) S.plan = { areaSelected: false, previewing: false, lots: 0 }; break;
        case "generatePreview": if (S.plan.previewing) S.seed++; S.plan = { ...S.plan, previewing: true }; break;
        case "clearSelection": S.plan = { areaSelected: false, previewing: false, lots: 0 }; break;
        case "AnarchyToggled": S.anarchy = !S.anarchy; break;
        case "build": S.status = `Construidos ${S.plan.lots} edificios (simulado).`; S.plan = { areaSelected: false, previewing: false, lots: 0 }; break;
    }
    emit();
}

/** La escena simulada avisa de que se ha elegido una zona. */
export function areaPicked(lots: number) { S.plan = { areaSelected: true, previewing: false, lots }; emit(); }
