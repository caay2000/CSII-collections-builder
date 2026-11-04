import React, { useEffect } from "react";
import styles from "./panel.module.scss";
import { actions, BuilderDto, useBuilder, useBuildings, useCollections, usePlan, useSelected } from "./bindings";
import { matches } from "./collectionsTab";
import { CollectionPicker, Slider, StepFilter } from "./widgets";

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

    return (
        <div className={styles.col}>
            <div className={styles.sec}>Area</div>
            <div className={styles.row}>
                <div className={styles.seg} style={{ flex: 1 }}>
                    <button className={b.mode === "block" ? styles.segOnWide : styles.segBtnWide} onClick={() => set({ mode: "block" })}>Block</button>
                    <button className={b.mode === "polygon" ? styles.segOnWide : styles.segBtnWide} onClick={() => set({ mode: "polygon" })}>Polygon</button>
                </div>
            </div>
            <div className={styles.hint}>
                {b.mode === "block"
                    ? "Click inside a block enclosed by roads: the whole zoned area is selected. Right click: clear."
                    : "Click to place corners and click the first point to close the polygon. Right click: undo last point."}
            </div>

            <div className={styles.sec}>Collection</div>
            <CollectionPicker items={collections} selected={selected} onSelect={actions.select} />

            <div className={styles.sec}>Filters</div>
            <StepFilter label="Lot width" op={b.widthOp} value={b.widthValue} onChange={(o, v) => set({ widthOp: o, widthValue: v })} />
            <StepFilter label="Lot depth" op={b.depthOp} value={b.depthValue} onChange={(o, v) => set({ depthOp: o, depthValue: v })} />
            <StepFilter label="Level" op={b.levelOp} value={b.levelValue} onChange={(o, v) => set({ levelOp: o, levelValue: v })} />
            <div className={styles.row}>
                <span className={styles.label}>Density</span>
                <Slider min={10} max={100} step={10} value={Math.max(10, Math.round(b.density / 10) * 10)} onChange={(v) => v !== b.density && set({ density: v })} />
                <span className={styles.value}>{`${Math.max(10, Math.round(b.density / 10) * 10)}%`}</span>
            </div>

            <div className={styles.status}>
                {selected ? <><b>{candidates.length}</b> of {buildings.length} buildings match</> : "Choose a collection."}
                {plan.areaSelected && <> · <b>{plan.lots}</b> lots to fill</>}
                {!plan.areaSelected && selected && <> · select an area in the world</>}
            </div>

            <button className={styles.btnPrimary} disabled={!canGenerate} onClick={() => actions.generatePreview()}>
                {plan.previewing ? "New combination" : "Generate preview"}
            </button>
            <button className={styles.btnClear} disabled={!plan.areaSelected} onClick={() => actions.clearSelection()}>Clear</button>
            <button className={styles.btnBuild} disabled={!plan.previewing} onClick={() => actions.build()}>Build</button>
        </div>
    );
};
