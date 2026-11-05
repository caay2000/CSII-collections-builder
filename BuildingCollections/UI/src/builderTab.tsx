import React, { useEffect } from "react";
import styles from "./panel.module.scss";
import { actions, BuilderDto, BuildingDto, useBuilder, useBuilderMessage, useBuilderMessageError, useBuildings, useCollections, useMixBuildings, usePlan, useSelected } from "./bindings";
import { matches } from "./collectionsTab";
import { CheckRow, CollectionPicker, icon, MixSlider, StepFilter, Stepper } from "./widgets";

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
    const message = useBuilderMessage();
    const messageError = useBuilderMessageError();
    const set = (patch: Partial<BuilderDto>) => actions.setBuilder({ ...b, ...patch });

    // Abrir la pestaña activa la herramienta de selección; cerrarla la desactiva (como StreetBuilder).
    useEffect(() => {
        actions.setToolActive(true);
        return () => actions.setToolActive(false);
    }, []);

    const mixBuildings = useMixBuildings();
    const passes = (x: BuildingDto) =>
        matches(b.widthOp, b.widthValue, x.w) && matches(b.depthOp, b.depthValue, x.d) && matches(b.levelOp, b.levelValue, x.level);
    // La mezcla solo cuenta si la segunda colección existe y no es la misma que la primera.
    const mix = b.mix && b.mix !== selected && collections.some((c) => c.name === b.mix) ? b.mix : "";
    const candidates = buildings.filter(passes);
    const mixCandidates = mix ? mixBuildings.filter(passes) : [];
    const canGenerate = plan.areaSelected && !!selected && candidates.length + mixCandidates.length > 0;
    // Por qué Preview está desactivado. Los problemas de la colección van en la línea de mensaje (tienen prioridad
    // sobre el último mensaje de la herramienta); "elige zona" ya lo dice el resumen, así que solo va en el tooltip.
    const collectionProblem = !selected ? null
        : buildings.length + (mix ? mixBuildings.length : 0) === 0 ? (mix ? "Both collections are empty." : "The collection is empty.")
        : candidates.length + mixCandidates.length === 0 ? "No building in the collection passes the filters."
        : null;
    const previewWhy = canGenerate ? undefined : !selected ? "Choose a collection." : collectionProblem ?? "Select an area in the world.";
    const line = collectionProblem ?? message;
    // Elegir como primera la que estaba de segunda las intercambia (y el reparto se invierte con ellas).
    const selectFirst = (n: string) => {
        if (mix && n === mix) set({ mix: selected, mixShare: 100 - b.mixShare });
        actions.select(n);
    };
    const density = Math.max(10, Math.min(100, Math.round(b.density / 10) * 10));

    return (
        <>
            <div className={styles.secCol}>
                <div className={styles.row}>
                    <span className={styles.lbl} style={{ flex: 1 }}>Collection</span>
                    <div className={styles.mixPick}>
                        {mix && <span className={styles.mixDotA} />}
                        <CollectionPicker items={collections} selected={selected} onSelect={selectFirst} />
                        {mix && <span className={styles.mixRemoveSpacer} />}
                    </div>
                </div>
                {mix ? (
                    <>
                        <div className={styles.row}>
                            <span className={styles.lbl} style={{ flex: 1 }}>Mixed with</span>
                            <div className={styles.mixPick}>
                                <span className={styles.mixDotB} />
                                <CollectionPicker items={collections.filter((c) => c.name !== selected)} selected={mix}
                                    onSelect={(n) => set({ mix: n })} />
                                <button className={styles.mixRemove} title="Stop mixing: use only the first collection" onClick={() => set({ mix: "" })}>
                                    <img src={icon("close")} />
                                </button>
                            </div>
                        </div>
                        <MixSlider left={selected} right={mix} share={b.mixShare} onChange={(v) => set({ mixShare: v })} />
                    </>
                ) : (
                    selected && collections.length >= 2 && (
                        <button className={styles.addMix} title="Fill the area with buildings from two collections, in the proportion you choose."
                            onClick={() => set({ mix: collections.find((c) => c.name !== selected)!.name, mixShare: 50 })}>
                            <img src={icon("plus")} />Add another collection
                        </button>
                    )
                )}
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
                    {plan.anarchyAvailable && (
                        // El interruptor del propio Anarchy (el mismo que su botón en las opciones de herramienta, que
                        // con esta herramienta no aparece). Cambia la siguiente vista previa: pulsa Reshuffle.
                        <CheckRow label="Anarchy" accent checked={plan.anarchy} onChange={() => actions.toggleAnarchy()}
                            title="Turns the Anarchy mod on or off (same as its own button). With it on, new buildings may overlap a little, so street ends close up. Existing buildings are never removed; at most the last joint touches them by the same overlap." />
                    )}
                    {plan.anarchy && (
                        // Con Anarchy, los edificios nuevos pueden pisarse para cerrar el sobrante de cada calle; cuánto
                        // (0,5 a 2 m por junta) se elige en las opciones del mod. Pulsa Reshuffle para aplicarlo.
                        <CheckRow label="Overlap buildings" accent checked={b.overlap > 0} onChange={(v) => set({ overlap: v ? 0.5 : 0 })}
                            title="Street lengths are rarely a multiple of 8 m. On: to close the leftover at the end of a street, one building is made a cell wider and the row is squeezed, each joint overlapping by up to the amount set in the mod's options (0.5 m by default). Off: no overlap, a single gap stays. Press Reshuffle to apply." />
                    )}
                </div>
            </div>

            <div className={styles.builderFilters}>
                <div className={styles.filterBox}>
                    <CheckRow label="Trees in leftover space" checked={b.trees} onChange={(v) => set({ trees: v })}
                        title="Base-game trees fill the leftover space (courtyards, gaps), away from sidewalks and buildings." />
                    {b.trees && plan.treeSets.length > 0 && (
                        // Dos filas: arriba "todos" y los sets con nombre (con icono); abajo los personalizados (1-5).
                        <>
                            <div className={styles.filter} title="Tree Controller set the trees are picked from (All: every base-game tree).">
                                <span className={styles.filterLabel}>Set</span>
                                <button className={b.treeSet === "" ? styles.chipOn : styles.chip} title="All base-game trees" onClick={() => set({ treeSet: "" })}>
                                    <img src="coui://uil/Standard/StarAll.svg" />
                                </button>
                                {plan.treeSets.filter((id) => TREE_SET_ICONS[id]).map((id) => (
                                    <button key={id} className={b.treeSet === id ? styles.chipOn : styles.chip} title={TREE_SET_NAMES[id] ?? id} onClick={() => set({ treeSet: id })}>
                                        <img src={TREE_SET_ICONS[id]} />
                                    </button>
                                ))}
                            </div>
                            {plan.treeSets.some((id) => !TREE_SET_ICONS[id]) && (
                                <div className={styles.filter} title="Your custom Tree Controller sets.">
                                    <span className={styles.filterLabel} />
                                    {plan.treeSets.filter((id) => !TREE_SET_ICONS[id]).map((id) => (
                                        <button key={id} className={b.treeSet === id ? styles.chipOn : styles.chip} title={TREE_SET_NAMES[id] ?? id} onClick={() => set({ treeSet: id })}>
                                            {id.slice(-1)}
                                        </button>
                                    ))}
                                </div>
                            )}
                        </>
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
                            <span className={styles.big}>{`${candidates.length + mixCandidates.length}`}<span className={styles.bigOf}>{` / ${buildings.length + (mix ? mixBuildings.length : 0)}`}</span></span>
                            <span className={styles.small}>{mix ? `buildings match · ${candidates.length} + ${mixCandidates.length}` : "buildings match"}</span>
                        </div>
                    </>
                )}
            </div>

            {line && (collectionProblem ? <div className={styles.builderMsgWarn}>{line}</div>
                : messageError ? <div key={messageError} className={styles.builderMsgError}>{line}</div>
                : <div className={styles.builderMsg}>{line}</div>)}

            <div className={styles.actions}>
                <button className={styles.btnClear} title="Clear selection (right-click)" disabled={!plan.areaSelected} onClick={() => actions.clearSelection()}>
                    <img src={icon("clear")} />
                </button>
                <button className={styles.btn} disabled={!canGenerate} title={previewWhy} onClick={() => actions.generatePreview()}>
                    <img src={icon("shuffle")} />{plan.previewing ? "Reshuffle" : "Preview"}
                </button>
                <button className={plan.previewing ? styles.btnPrimary : styles.btn} disabled={!plan.previewing} onClick={() => actions.build()}>
                    <img src={icon("build")} />Build
                </button>
            </div>
        </>
    );
};
