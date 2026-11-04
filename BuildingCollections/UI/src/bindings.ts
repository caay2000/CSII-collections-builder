import { bindValue, trigger, useValue } from "cs2/api";

const G = "BuildingCollections";

export interface CollectionDto { name: string; tag: string; count: number }
/** Un edificio de la colección seleccionada. Los datos salen del propio juego; pack solo si Find It está. */
export interface BuildingDto {
    id: string; name: string; w: number; d: number; level: number; icon: string;
    /** Árbol o planta: va en la decoración, no en las parcelas. */
    decor?: boolean;
}
export interface BuilderDto {
    mode: "block" | "polygon";
    widthOp: number; widthValue: number;
    depthOp: number; depthValue: number;
    levelOp: number; levelValue: number;
    density: number;
    leaveGaps: boolean;
    /** Árboles del juego base en el espacio sobrante. */
    trees: boolean;
    decorDensity: number;
}
export interface PlanDto { areaSelected: boolean; previewing: boolean; lots: number; blocks: number }

export const DEFAULT_BUILDER: BuilderDto = {
    mode: "block", widthOp: 0, widthValue: 0, depthOp: 0, depthValue: 0, levelOp: 0, levelValue: 0, density: 100, leaveGaps: false, trees: false, decorDensity: 50,
};

const panelOpen$ = bindValue<boolean>(G, "panelOpen", false);
const toolActive$ = bindValue<boolean>(G, "toolActive", false);
const findIt$ = bindValue<boolean>(G, "findItAvailable", false);
const collections$ = bindValue<string>(G, "collections", "[]");
const buildings$ = bindValue<string>(G, "buildings", "[]");
const selected$ = bindValue<string>(G, "selected", "");
const status$ = bindValue<string>(G, "status", "");
const builder$ = bindValue<string>(G, "builder", "{}");
const plan$ = bindValue<string>(G, "plan", "{}");

function parse<T>(raw: string, fallback: T): T {
    try { return JSON.parse(raw) as T; } catch { return fallback; }
}

export const usePanelOpen = () => useValue(panelOpen$);
export const useToolActive = () => useValue(toolActive$);
export const useFindIt = () => useValue(findIt$);
export const useSelected = () => useValue(selected$);
export const useStatus = () => useValue(status$);
export const useCollections = (): CollectionDto[] => parse(useValue(collections$), []);
export const useBuildings = (): BuildingDto[] => parse(useValue(buildings$), []);
export const useBuilder = (): BuilderDto => ({ ...DEFAULT_BUILDER, ...parse<Partial<BuilderDto>>(useValue(builder$), {}) });
export const usePlan = (): PlanDto => ({ areaSelected: false, previewing: false, lots: 0, blocks: 0, ...parse<Partial<PlanDto>>(useValue(plan$), {}) });

export const actions = {
    setPanelOpen: (v: boolean) => trigger(G, "setPanelOpen", v),
    select: (name: string) => trigger(G, "select", name),
    create: (name: string) => trigger(G, "create", name),
    clear: () => trigger(G, "clear"),
    remove: () => trigger(G, "delete"),
    clone: () => trigger(G, "clone"),
    rename: (name: string) => trigger(G, "rename", name),
    removeBuildings: (ids: string[]) => trigger(G, "removeBuildings", JSON.stringify(ids)),
    addAllFromFindIt: () => trigger(G, "addAll"),
    setBuilder: (b: BuilderDto) => trigger(G, "setBuilder", JSON.stringify(b)),
    setToolActive: (v: boolean) => trigger(G, "setToolActive", v),
    generatePreview: () => trigger(G, "generatePreview"),
    clearSelection: () => trigger(G, "clearSelection"),
    build: () => trigger(G, "build"),
};
