import { useMemo } from "react";
import { bindValue, trigger, useValue } from "cs2/api";

const G = "BuildingCollections";

export interface CollectionDto { name: string; tag: string; count: number }
export interface BuildingDto {
    id: string; name: string; w: number; d: number; level: number; icon: string;
}
export interface BuilderDto {
    mode: "block" | "polygon";
    widthOp: number; widthValue: number;
    depthOp: number; depthValue: number;
    levelOp: number; levelValue: number;
    density: number;
    trees: boolean;
    decorDensity: number;
    overlapMeters: number;
    treeSet: string;
    treeAges: number;
    mix: string;
    mixShare: number;
}
export interface PlanDto {
    areaSelected: boolean; previewing: boolean; lots: number; blocks: number; anarchy: boolean; anarchyAvailable: boolean;
    filled: number; front: number;
    treeSets: string[];
}

export const DEFAULT_BUILDER: BuilderDto = {
    mode: "block", widthOp: 0, widthValue: 0, depthOp: 0, depthValue: 0, levelOp: 0, levelValue: 0, density: 100, trees: false, decorDensity: 50, overlapMeters: 0.5, treeSet: "", treeAges: 4, mix: "", mixShare: 50,
};

const panelOpen$ = bindValue<boolean>(G, "panelOpen", false);
const toolActive$ = bindValue<boolean>(G, "toolActive", false);
const findIt$ = bindValue<boolean>(G, "findItAvailable", false);
const collections$ = bindValue<string>(G, "collections", "[]");
const buildings$ = bindValue<string>(G, "buildings", "[]");
const mixBuildings$ = bindValue<string>(G, "mixBuildings", "[]");
const selected$ = bindValue<string>(G, "selected", "");
const status$ = bindValue<string>(G, "status", "");
const builder$ = bindValue<string>(G, "builder", "{}");
const plan$ = bindValue<string>(G, "plan", "{}");
const escapes$ = bindValue<number>(G, "escapes", 0);
const builderMessage$ = bindValue<string>(G, "builderMessage", "");
const builderMessageError$ = bindValue<number>(G, "builderMessageError", 0);
const plopping$ = bindValue<string>(G, "plopping", "");

function parse<T>(raw: string, fallback: T): T {
    try { return JSON.parse(raw) as T; } catch { return fallback; }
}

export const usePanelOpen = () => useValue(panelOpen$);
export const useEscapes = () => useValue(escapes$);
export const useToolActive = () => useValue(toolActive$);
export const useFindIt = () => useValue(findIt$);
export const useSelected = () => useValue(selected$);
export const useStatus = () => useValue(status$);
export const useBuilderMessage = () => useValue(builderMessage$);
export const useBuilderMessageError = () => useValue(builderMessageError$);
export const usePlopping = () => useValue(plopping$);
function useJson<T>(raw: string, fallback: T): T {
    return useMemo(() => parse(raw, fallback), [raw]);
}

export const useCollections = (): CollectionDto[] => useJson(useValue(collections$), []);
export const useBuildings = (): BuildingDto[] => useJson(useValue(buildings$), []);
export const useMixBuildings = (): BuildingDto[] => useJson(useValue(mixBuildings$), []);
export const useBuilder = (): BuilderDto => ({ ...DEFAULT_BUILDER, ...parse<Partial<BuilderDto>>(useValue(builder$), {}) });
export const usePlan = (): PlanDto => ({ areaSelected: false, previewing: false, lots: 0, blocks: 0, anarchy: false, anarchyAvailable: false, filled: 0, front: 0, treeSets: [], ...parse<Partial<PlanDto>>(useValue(plan$), {}) });

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
    plop: (id: string) => trigger(G, "plop", id),
    setShown: (ids: string[]) => trigger(G, "setShown", JSON.stringify(ids)),
};
