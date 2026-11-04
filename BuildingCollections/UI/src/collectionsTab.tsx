import React, { useCallback, useMemo, useState } from "react";
import styles from "./panel.module.scss";
import { actions, useBuildings, useCollections, useFindIt, useSelected, useStatus } from "./bindings";
import { icon, IconButton, InlineInput, StepFilter } from "./widgets";
import { Scrollable } from "cs2/ui";

/** Nombre legible como el de Find It: "CCP5_ResidentialMediumLeft01_L1_2x2" -> "CCP5 Residential Medium Left01 L1 2x2". */
export function prettyName(n: string): string {
    return n.replace(/_/g, " ").replace(/([a-z])([A-Z])/g, "$1 $2").replace(/\s+/g, " ").trim();
}

export function matches(op: number, value: number, n: number): boolean {
    if (!value) return true;
    return op === 0 ? n === value : op === 1 ? n !== value : op === 2 ? n > value : n < value;
}

type Editing = "" | "new" | "rename" | "delete" | "removeAll";

export const CollectionsTab = () => {
    const collections = useCollections();
    const selected = useSelected();
    const buildings = useBuildings();
    const findIt = useFindIt();
    const status = useStatus();
    const [editing, setEditing] = useState<Editing>("");
    const [q, setQ] = useState("");
    const [f, setF] = useState({ wOp: 0, wV: 0, dOp: 0, dV: 0, lOp: 0, lV: 0 });
    const cancel = useCallback(() => setEditing(""), []);

    const shown = useMemo(
        () => buildings.filter((b) =>
            matches(f.wOp, f.wV, b.w) && matches(f.dOp, f.dV, b.d) && matches(f.lOp, f.lV, b.level) &&
            (b.name.toLowerCase().includes(q.toLowerCase()) || prettyName(b.name).toLowerCase().includes(q.toLowerCase()))),
        [buildings, f, q]);
    const current = collections.find((c) => c.name === selected);
    const filtered = !!q || f.wV > 0 || f.dV > 0 || f.lV > 0;

    return (
        <div className={styles.split}>
            <div className={styles.side}>
                <div className={styles.sideHead}>
                    <span className={styles.secLabel}>Collections</span>
                    <IconButton name="plus" title="New collection" className={styles.iconBtnSmall}
                        onClick={() => setEditing(editing === "new" ? "" : "new")} />
                </div>
                {editing === "new" && (
                    <InlineInput placeholder="Name, Enter to create" onCancel={cancel}
                        onSubmit={(v) => { actions.create(v); setEditing(""); }} />
                )}
                <Scrollable className={styles.list} vertical trackVisibility="scrollable">
                    {collections.length === 0 && <div className={styles.empty}>No collections yet. Use + to create one.</div>}
                    {collections.map((c) => (
                        <div key={c.name} className={c.name === selected ? styles.itemOn : styles.item}
                            onClick={() => { setEditing(""); actions.select(c.name); }}>
                            <span className={styles.itemName}>{c.name}</span><span className={styles.itemCount}>{c.count}</span>
                        </div>
                    ))}
                </Scrollable>
                {editing === "rename" && current && (
                    <InlineInput initial={current.name} placeholder="New name, Enter to rename" onCancel={cancel}
                        onSubmit={(v) => { if (v !== current.name) actions.rename(v); setEditing(""); }} />
                )}
                {editing === "delete" && current && (
                    <div className={styles.confirm}>
                        <span>{`Delete "${current.name}"?`}</span>
                        <div className={styles.confirmBtns}>
                            <button className={styles.smallBtnDanger} onClick={() => { actions.remove(); setEditing(""); }}>Delete</button>
                            <button className={styles.smallBtn} onClick={cancel}>Cancel</button>
                        </div>
                    </div>
                )}
                <div className={styles.toolbar}>
                    <IconButton name="copy" title="Duplicate collection" disabled={!current} onClick={() => { setEditing(""); actions.clone(); }} />
                    <IconButton name="rename" title="Rename collection" disabled={!current} onClick={() => setEditing("rename")} />
                    <IconButton name="trash" title="Delete collection" className={styles.iconBtnDanger} disabled={!current} onClick={() => setEditing("delete")} />
                </div>
                {status && <div className={status === "OK" ? styles.ok : styles.warn}>{status}</div>}
            </div>

            <div className={styles.main}>
                <div className={styles.searchRow}>
                    <div className={styles.search}>
                        <img src={icon("search")} />
                        <input className={styles.searchInput} placeholder={`Search in ${current ? current.name : "collection"}`}
                            value={q} onChange={(e) => setQ(e.target.value)} />
                    </div>
                    <span className={styles.count}>
                        {current ? (filtered ? `${shown.length} of ${buildings.length}` : `${buildings.length} buildings`) : ""}
                    </span>
                </div>
                <div className={styles.filterBox}>
                    <StepFilter label="Lot Width" op={f.wOp} value={f.wV} onChange={(o, v) => setF({ ...f, wOp: o, wV: v })} />
                    <StepFilter label="Lot Depth" op={f.dOp} value={f.dV} onChange={(o, v) => setF({ ...f, dOp: o, dV: v })} />
                    <StepFilter label="Building Level" op={f.lOp} value={f.lV} onChange={(o, v) => setF({ ...f, lOp: o, lV: v })} />
                </div>
                <Scrollable className={styles.gridScroll} vertical trackVisibility="scrollable">
                    <div className={styles.grid}>
                        {shown.map((b) => {
                            // El tamaño y el nivel ya van en la tarjeta: se quitan del final del nombre.
                            const name = prettyName(b.name).replace(/(\s+(L\d|\d+x\d+))+$/, "");
                            return (
                                <div key={b.id} className={styles.card} title={`${b.name} · level ${b.level}`}>
                                    <img className={styles.thumb} src={b.icon} />
                                    {b.w > 0 && <span className={styles.sizeTag}>{`${b.w}×${b.d}`}</span>}
                                    <div className={styles.pips}>
                                        {[1, 2, 3, 4, 5].map((n) => <span key={n} className={n <= b.level ? styles.pipOn : styles.pip} />)}
                                    </div>
                                    <div className={styles.cardName}>{name}</div>
                                    <button className={styles.remove} title="Remove from collection" onClick={() => actions.removeBuildings([b.id])}>
                                        <img src={icon("remove")} />
                                    </button>
                                </div>
                            );
                        })}
                        {current && shown.length === 0 && (
                            <div className={styles.empty}>{buildings.length === 0 ? "Empty collection. Add buildings with Shift+click in Find It." : "Nothing matches these filters."}</div>
                        )}
                        {!current && <div className={styles.empty}>Create or choose a collection.</div>}
                    </div>
                </Scrollable>
                {editing === "removeAll" && current ? (
                    <div className={styles.confirm}>
                        <span>{`Remove all ${buildings.length} buildings from "${current.name}"?`}</span>
                        <div className={styles.confirmBtns}>
                            <button className={styles.smallBtnDanger} onClick={() => { actions.clear(); setEditing(""); }}>Remove all</button>
                            <button className={styles.smallBtn} onClick={cancel}>Cancel</button>
                        </div>
                    </div>
                ) : (
                    <div className={styles.footer}>
                        <div className={styles.footerAdd}>
                            <button className={styles.addAll} disabled={!current || !findIt}
                                title={findIt ? "Adds every building Find It is showing now (its current search and filters)" : "Find It not detected"}
                                onClick={() => actions.addAllFromFindIt()}>
                                <img src={icon("add-all")} />Add all shown in Find It
                            </button>
                            <div className={styles.footerHint}>
                                <span className={styles.kbd}>Shift</span>
                                <span>+ click in Find It adds one building</span>
                            </div>
                        </div>
                        <button className={styles.textDanger} disabled={shown.length === 0}
                            onClick={() => filtered ? actions.removeBuildings(shown.map((b) => b.id)) : setEditing("removeAll")}>
                            {filtered ? `Remove ${shown.length} shown` : "Remove all"}
                        </button>
                    </div>
                )}
            </div>
        </div>
    );
};
