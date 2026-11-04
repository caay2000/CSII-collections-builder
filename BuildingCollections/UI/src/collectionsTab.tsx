import React, { useMemo, useState } from "react";
import styles from "./panel.module.scss";
import { actions, useBuildings, useCollections, useFindIt, useSelected, useStatus } from "./bindings";
import { StepFilter } from "./widgets";

/** Nombre legible como el de Find It: "CCP5_ResidentialMediumLeft01_L1_2x2" -> "CCP5 Residential Medium Left01 L1 2x2". */
export function prettyName(n: string): string {
    return n.replace(/_/g, " ").replace(/([a-z])([A-Z])/g, "$1 $2").replace(/\s+/g, " ").trim();
}

export function matches(op: number, value: number, n: number): boolean {
    if (!value) return true;
    return op === 0 ? n === value : op === 1 ? n !== value : op === 2 ? n > value : n < value;
}

export const CollectionsTab = () => {
    const collections = useCollections();
    const selected = useSelected();
    const buildings = useBuildings();
    const findIt = useFindIt();
    const status = useStatus();
    const [name, setName] = useState("");
    const [confirm, setConfirm] = useState<"" | "clear" | "delete">("");
    const [q, setQ] = useState("");
    const [f, setF] = useState({ wOp: 0, wV: 0, dOp: 0, dV: 0, lOp: 0, lV: 0 });

    const shown = useMemo(
        () => buildings.filter((b) =>
            matches(f.wOp, f.wV, b.w) && matches(f.dOp, f.dV, b.d) && matches(f.lOp, f.lV, b.level) &&
            (b.name.toLowerCase().includes(q.toLowerCase()) || prettyName(b.name).toLowerCase().includes(q.toLowerCase()))),
        [buildings, f, q]);
    const current = collections.find((c) => c.name === selected);

    return (
        <div className={styles.split}>
            <div className={styles.side}>
                <div className={styles.row}>
                    <input className={styles.input} value={name} placeholder="New collection" onChange={(e) => setName(e.target.value)} />
                    <button className={styles.btn} onClick={() => { actions.create(name); setName(""); }}>Create</button>
                </div>
                <div className={styles.list}>
                    {collections.length === 0 && <div className={styles.muted}>No collections yet.</div>}
                    {collections.map((c) => (
                        <div key={c.name} className={c.name === selected ? styles.itemOn : styles.item}
                            onClick={() => { setConfirm(""); actions.select(c.name); }}>
                            <span>{c.name}</span><span className={styles.muted}>{c.count}</span>
                        </div>
                    ))}
                </div>
                <button className={styles.btn} disabled={!current || !findIt} onClick={() => actions.addAllFromFindIt()}>
                    Add all Find It items
                </button>
                {!findIt && <div className={styles.warn}>Find It not detected.</div>}
                <div className={styles.row}>
                    {confirm === "" ? (
                        <>
                            <button className={styles.btn} disabled={!current} onClick={() => actions.clone()}>Clone</button>
                            <button className={styles.btn} disabled={!current} onClick={() => setConfirm("clear")}>Empty</button>
                            <button className={styles.btnDanger} disabled={!current} onClick={() => setConfirm("delete")}>Delete</button>
                        </>
                    ) : (
                        <>
                            <span className={styles.muted}>{confirm === "clear" ? "Empty it?" : "Delete it?"}</span>
                            <button className={styles.btnDanger} onClick={() => { confirm === "clear" ? actions.clear() : actions.remove(); setConfirm(""); }}>Yes</button>
                            <button className={styles.btn} onClick={() => setConfirm("")}>No</button>
                        </>
                    )}
                </div>
                {status && <div className={status === "OK" ? styles.ok : styles.warn}>{status}</div>}
            </div>

            <div className={styles.main}>
                <div className={styles.row}>
                    <span className={styles.title2}>{current ? current.name : "No collection"}</span>
                    <span className={styles.muted}>{current ? `${shown.length} of ${buildings.length} shown` : ""}</span>
                </div>
                <input className={styles.input} placeholder="Search in collection" value={q} onChange={(e) => setQ(e.target.value)} />
                <StepFilter label="Lot width" op={f.wOp} value={f.wV} onChange={(o, v) => setF({ ...f, wOp: o, wV: v })} />
                <StepFilter label="Lot depth" op={f.dOp} value={f.dV} onChange={(o, v) => setF({ ...f, dOp: o, dV: v })} />
                <StepFilter label="Level" op={f.lOp} value={f.lV} onChange={(o, v) => setF({ ...f, lOp: o, lV: v })} />
                <div className={styles.grid}>
                    {shown.map((b) => {
                        const name = prettyName(b.name);
                        const size = `${b.w}x${b.d}`;
                        const label = b.w > 0 && !name.endsWith(size) ? `${name} ${size}` : name;
                        return (
                            <div key={b.id} className={styles.card} title={`${b.name} · level ${b.level}`}>
                                <img className={styles.thumb} src={b.icon} />
                                <div className={styles.cardName}>{label}</div>
                                {b.level > 0 && <span className={styles.lvl}>{`L${b.level}`}</span>}
                                <button className={styles.remove} title="Remove from collection" onClick={() => actions.removeBuildings([b.id])}>×</button>
                            </div>
                        );
                    })}
                    {current && shown.length === 0 && (
                        <div className={styles.muted}>{buildings.length === 0 ? "Empty collection. Add buildings with Shift+click in Find It." : "Nothing matches these filters."}</div>
                    )}
                </div>
                <div className={styles.row}>
                    <button className={styles.btnDanger} disabled={shown.length === 0}
                        onClick={() => actions.removeBuildings(shown.map((b) => b.id))}>
                        Remove {shown.length === buildings.length ? "all" : "shown"} ({shown.length})
                    </button>
                    <span className={styles.hint}>Shift+click a building in Find It to add it to this collection.</span>
                </div>
            </div>
        </div>
    );
};
