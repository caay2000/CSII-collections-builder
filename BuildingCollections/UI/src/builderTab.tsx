import React, { useEffect } from "react";
import styles from "./panel.module.scss";
import { actions, BuilderDto, useBuilder, useBuildings, useCollections, usePlan, useSelected } from "./bindings";
import { matches } from "./collectionsTab";
import { CheckRow, CollectionPicker, icon, StepFilter, Stepper } from "./widgets";

const TREE_SET_ICONS: Record<string, string> = {
    "YYTC-wild-deciduous-trees": "coui://uil/Standard/TreesDeciduous.svg",
    "YYTC-evergreen-trees": "coui://uil/Standard/TreesNeedle.svg",
    "YYTC-wild-bushes": "coui://uil/Standard/Bushes.svg",
};
const TREE_SET_NAMES: Record<string, string> = {
    "YYTC-wild-deciduous-trees": "Deciduous trees (Tree Controller)",
    "YYTC-evergreen-trees": "Evergreen trees (Tree Controller)",
    "YYTC-wild-bushes": "Wild bushes (Tree Controller)",
    "YYTC-custom-set-1": "Custom set 1 (Tree Controller)",
    "YYTC-custom-set-2": "Custom set 2 (Tree Controller)",
    "YYTC-custom-set-3": "Custom set 3 (Tree Controller)",
    "YYTC-custom-set-4": "Custom set 4 (Tree Controller)",
    "YYTC-custom-set-5": "Custom set 5 (Tree Controller)",
};
// Mismos bits que Game.Tools.AgeMask; iconos del juego (los que usa Tree Controller).
const AGES = [
    { bit: 1, name: "Child", icon: "Media/Tools/Vegetation Options/TreeChild.svg" },
    { bit: 2, name: "Teen", icon: "Media/Tools/Vegetation Options/TreeTeen.svg" },
    { bit: 4, name: "Adult", icon: "Media/Tools/Vegetation Options/TreeAdult.svg" },
    { bit: 8, name: "Elderly", icon: "Media/Tools/Vegetation Options/TreeElderly.svg" },
];

export const BuilderTab = () => {
    const b = useBuilder();
    const collections = useCollections();
    const selected = useSelected();
    const buildings = useBuildings();
    const plan = usePlan();
    const set = (patch: Partial<BuilderDto>) => actions.setBuilder({ ...b, ...patch });

    // Abrir la pestaña activa la herramienta de selección; cerrarla la desactiva (como StreetBuilder).
    useEffect(() => {
        actions.setToolActive(true);
        return () => actions.setToolActive(false);
    }, []);

    const candidates = buildings.filter((x) =>
        matches(b.widthOp, b.widthValue, x.w) && matches(b.depthOp, b.depthValue, x.d) && matches(b.levelOp, b.levelValue, x.level));
    const canGenerate = plan.areaSelected && !!selected && candidates.length > 0;
    const density = Math.max(10, Math.min(100, Math.round(b.density / 10) * 10));

    return (
        <>
            <div className={styles.sec}>
                <span className={styles.lbl}>Collection</span>
                <CollectionPicker items={collections} selected={selected} onSelect={actions.select} />
            </div>

            <div className={styles.secCol}>
                <span className={styles.lbl}>Area</span>
                <div className={styles.modes}>
                    <button className={b.mode === "block" ? styles.modeOn : styles.mode} onClick={() => set({ mode: "block" })}>
                        <img src={icon("block")} />Block
                    </button>
                    <button className={b.mode === "polygon" ? styles.modeOn : styles.mode} onClick={() => set({ mode: "polygon" })}>
                        <img src={icon("polygon")} />Polygon
                    </button>
                </div>
                <span className={styles.hint}>
                    {b.mode === "block"
                        ? "Click inside a block enclosed by roads: everything inside is selected. Right-click clears."
                        : "Click to place corners (they snap to the sidewalk), click the first one to close. Right-click undoes the last corner."}
                </span>
            </div>

            <div className={styles.builderFilters}>
                <div className={styles.filterBox}>
                    <StepFilter label="Lot Width" op={b.widthOp} value={b.widthValue} onChange={(o, v) => set({ widthOp: o, widthValue: v })} />
                    <StepFilter label="Lot Depth" op={b.depthOp} value={b.depthValue} onChange={(o, v) => set({ depthOp: o, depthValue: v })} />
                    <StepFilter label="Building Level" op={b.levelOp} value={b.levelValue} onChange={(o, v) => set({ levelOp: o, levelValue: v })} />
                    <div className={styles.filter}>
                        <span className={styles.filterLabel}>Density</span>
                        <Stepper value={`${density}%`}
                            onDown={() => set({ density: Math.max(10, density - 10) })}
                            onUp={() => set({ density: Math.min(100, density + 10) })} />
                    </div>
                    <CheckRow label="Leave gaps" checked={b.leaveGaps} onChange={(v) => set({ leaveGaps: v })}
                        title="Off: fill the whole road front with the sizes available. On: pick buildings at random and leave gaps." />
                    {plan.anarchyAvailable && (
                        // El interruptor del propio Anarchy (el mismo que su botón en las opciones de herramienta, que
                        // con esta herramienta no aparece). Cambia la siguiente vista previa: pulsa Reshuffle.
                        <CheckRow label="Anarchy" accent checked={plan.anarchy} onChange={() => actions.toggleAnarchy()}
                            title="Turns the Anarchy mod on or off (same as its own button). With it on, new buildings may overlap a little, so street ends close up. Existing buildings are never removed; at most the last joint touches them by the same overlap." />
                    )}
                    {plan.anarchy && (
                        <div className={styles.filter} title="How much each joint between two new buildings may overlap. Street lengths are rarely a multiple of 8 m: to close the last gap, one building is made a cell wider and the row is squeezed, spreading the overlap over all its joints. Press Reshuffle to apply.">
                            <span className={styles.filterLabelAnarchy}>Max overlap</span>
                            <Stepper value={b.overlap === 0 ? "None" : `${b.overlap.toFixed(1)} m`}
                                onDown={() => set({ overlap: Math.max(0, b.overlap - 0.5) })}
                                onUp={() => set({ overlap: Math.min(3.5, b.overlap + 0.5) })} />
                        </div>
                    )}
                </div>
            </div>

            <div className={styles.builderFilters}>
                <div className={styles.filterBox}>
                    <CheckRow label="Trees in leftover space" checked={b.trees} onChange={(v) => set({ trees: v })}
                        title="Base-game trees fill the leftover space (courtyards, gaps), away from sidewalks and buildings." />
                    {b.trees && plan.treeSets.length > 0 && (
                        <div className={styles.filter} title="Tree Controller set the trees are picked from (All: every base-game tree).">
                            <span className={styles.filterLabel}>Set</span>
                            <button className={b.treeSet === "" ? styles.chipOn : styles.chip} title="All base-game trees" onClick={() => set({ treeSet: "" })}>
                                <img src="coui://uil/Standard/StarAll.svg" />
                            </button>
                            {plan.treeSets.map((id) => (
                                <button key={id} className={b.treeSet === id ? styles.chipOn : styles.chip} title={TREE_SET_NAMES[id] ?? id} onClick={() => set({ treeSet: id })}>
                                    {TREE_SET_ICONS[id] ? <img src={TREE_SET_ICONS[id]} /> : id.slice(-1)}
                                </button>
                            ))}
                        </div>
                    )}
                    {b.trees && (
                        <div className={styles.filter} title="Tree ages to plant (at least one).">
                            <span className={styles.filterLabel}>Age</span>
                            {AGES.map((a) => (
                                <button key={a.bit} className={(b.treeAges & a.bit) !== 0 ? styles.chipOn : styles.chip} title={a.name}
                                    onClick={() => { const v = b.treeAges ^ a.bit; if (v !== 0) set({ treeAges: v }); }}>
                                    <img src={a.icon} />
                                </button>
                            ))}
                        </div>
                    )}
                    {b.trees && (
                        <div className={styles.filter}>
                            <span className={styles.filterLabel}>Tree density</span>
                            <Stepper value={`${b.decorDensity}%`}
                                onDown={() => set({ decorDensity: Math.max(10, b.decorDensity - 10) })}
                                onUp={() => set({ decorDensity: Math.min(100, b.decorDensity + 10) })} />
                        </div>
                    )}
                </div>
            </div>

            <div className={styles.summary}>
                {!selected ? (
                    <span className={styles.summaryEmpty}>Choose a collection.</span>
                ) : (
                    <>
                        <div className={styles.summaryCol}>
                            {plan.areaSelected && plan.previewing && plan.front > 0 ? (
                                <>
                                    <span className={styles.bigAccent}>{`${plan.filled}`}<span className={styles.bigOf}>{` / ${plan.front}`}</span></span>
                                    <span className={styles.small}>{`street cells filled · ${Math.round(100 * plan.filled / plan.front)}%`}</span>
                                </>
                            ) : plan.areaSelected ? (
                                <>
                                    <span className={styles.bigAccent}>{`${plan.lots}`}</span>
                                    <span className={styles.small}>{plan.blocks > 0 ? `street cells · ${plan.blocks} blocks` : "street cells"}</span>
                                </>
                            ) : (
                                <span className={styles.summaryEmpty}>Select an area in the world.</span>
                            )}
                        </div>
                        <div className={styles.summaryCol}>
                            <span className={styles.big}>{`${candidates.length}`}<span className={styles.bigOf}>{` / ${buildings.length}`}</span></span>
                            <span className={styles.small}>buildings match</span>
                        </div>
                    </>
                )}
            </div>


            <div className={styles.actions}>
                <button className={styles.btnClear} title="Clear selection (right-click)" disabled={!plan.areaSelected} onClick={() => actions.clearSelection()}>
                    <img src={icon("clear")} />
                </button>
                <button className={styles.btn} disabled={!canGenerate} onClick={() => actions.generatePreview()}>
                    <img src={icon("shuffle")} />{plan.previewing ? "Reshuffle" : "Preview"}
                </button>
                <button className={plan.previewing ? styles.btnPrimary : styles.btn} disabled={!plan.previewing} onClick={() => actions.build()}>
                    <img src={icon("build")} />Build
                </button>
            </div>
        </>
    );
};
