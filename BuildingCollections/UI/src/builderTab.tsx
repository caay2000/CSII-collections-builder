import React, { useEffect } from "react";
import styles from "./panel.module.scss";
import { actions, BuilderDto, useBuilder, useBuildings, useCollections, usePlan, useSelected } from "./bindings";
import { matches } from "./collectionsTab";
import { CheckRow, CollectionPicker, icon, StepFilter, Stepper } from "./widgets";

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

    const candidates = buildings.filter((x) => !x.decor &&
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
                </div>
            </div>

            <div className={styles.sec}>
                <span className={styles.lbl}>Decor</span>
                <CollectionPicker items={collections} selected={b.decor} noneLabel="No decor" onSelect={(n) => set({ decor: n })} />
            </div>
            {b.decor && (
                <div className={styles.builderFilters}>
                    <div className={styles.filterBox}>
                        <div className={styles.filter} title="Trees and plants from this collection fill the leftover space (courtyards, gaps). Add them with Shift+click in Find It.">
                            <span className={styles.filterLabel}>Decor density</span>
                            <Stepper value={`${b.decorDensity}%`}
                                onDown={() => set({ decorDensity: Math.max(10, b.decorDensity - 10) })}
                                onUp={() => set({ decorDensity: Math.min(100, b.decorDensity + 10) })} />
                        </div>
                    </div>
                </div>
            )}

            <div className={styles.summary}>
                {!selected ? (
                    <span className={styles.summaryEmpty}>Choose a collection.</span>
                ) : (
                    <>
                        <div className={styles.summaryCol}>
                            {plan.areaSelected ? (
                                <>
                                    <span className={styles.bigAccent}>{`${plan.lots}`}</span>
                                    <span className={styles.small}>{plan.blocks > 0 ? `lots to fill · ${plan.blocks} blocks` : "lots to fill"}</span>
                                </>
                            ) : (
                                <span className={styles.summaryEmpty}>Select an area in the world.</span>
                            )}
                        </div>
                        <div className={styles.summaryCol}>
                            <span className={styles.big}>{`${candidates.length}`}<span className={styles.bigOf}>{` / ${buildings.filter((x) => !x.decor).length}`}</span></span>
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
