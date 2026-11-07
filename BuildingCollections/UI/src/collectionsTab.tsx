import React, { memo, useCallback, useEffect, useMemo, useRef, useState } from "react";
import styles from "./panel.module.scss";
import { actions, BuildingDto, useBuildings, useCollections, useFindIt, usePlopping, useSelected, useStatus } from "./bindings";
import { icon, IconButton, InlineInput, StepFilter } from "./widgets";
import { Scrollable, ScrollController } from "cs2/ui";

export function prettyName(n: string): string {
    return n.replace(/_/g, " ").replace(/([a-z])([A-Z])/g, "$1 $2").replace(/\s+/g, " ").trim();
}

export function matches(op: number, value: number, n: number): boolean {
    if (!value) return true;
    return op === 0 ? n === value : op === 1 ? n !== value : op === 2 ? n > value : n < value;
}

type Editing = "" | "new" | "rename" | "delete" | "removeAll";

const Card = memo(({ b, label, on }: { b: BuildingDto; label: string; on: boolean }) => (
    <div className={on ? `${styles.card} ${styles.cardOn}` : styles.card} title={b.w > 0 ? `${b.name} · level ${b.level}. Click to place it.` : b.name}
        onClick={() => { if (b.w > 0) actions.plop(b.id); }}>
        <img className={styles.thumb} src={b.icon} />
        {b.w > 0 && <span className={styles.sizeTag}>{`${b.w}×${b.d}`}</span>}
        <div className={styles.pips}>
            {[1, 2, 3, 4, 5].map((n) => <span key={n} className={n <= b.level ? styles.pipOn : styles.pip} />)}
        </div>
        <div className={styles.cardName}>{label}</div>
        <button className={styles.remove} title="Remove from collection" onClick={(e) => { e.stopPropagation(); actions.removeBuildings([b.id]); }} />
    </div>
), (a, c) => a.on === c.on && a.label === c.label && a.b.id === c.b.id && a.b.icon === c.b.icon && a.b.level === c.b.level && a.b.w === c.b.w && a.b.d === c.b.d);

const FIRST_BATCH = 40;
const BATCH = 60;

const CARD_W = 86;
const CARD_H = 134;
const SPARE_ROWS = 2;

function useScrollController(): ScrollController | undefined {
    return useMemo(() => {
        try { return new ScrollController(); } catch { return undefined; }
    }, []);
}

export const CollectionsTab = () => {
    const collections = useCollections();
    const selected = useSelected();
    const buildings = useBuildings();
    const findIt = useFindIt();
    const status = useStatus();
    const plopping = usePlopping();
    const [editing, setEditing] = useState<Editing>("");
    const [q, setQ] = useState("");
    const [f, setF] = useState({ wOp: 0, wV: 0, dOp: 0, dV: 0, lOp: 0, lV: 0 });
    const cancel = useCallback(() => setEditing(""), []);

    const indexed = useMemo(() => buildings.map((b) => {
        const pretty = prettyName(b.name);
        return {
            b,
            label: pretty.replace(/(\s+(L\d|\d+x\d+))+$/, ""),
            search: (b.name + "\u0000" + pretty).toLowerCase(),
        };
    }), [buildings]);
    const shown = useMemo(() => {
        const needle = q.toLowerCase();
        return indexed.filter(({ b, search }) =>
            matches(f.wOp, f.wV, b.w) && matches(f.dOp, f.dV, b.d) && matches(f.lOp, f.lV, b.level) && search.includes(needle));
    }, [indexed, f, q]);
    const shownIds = useMemo(() => shown.map(({ b }) => b.id), [shown]);
    useEffect(() => { actions.setShown(shownIds); }, [shownIds]);
    const listKey = `${selected}\u0000${q}\u0000${JSON.stringify(f)}`;
    const [batch, setBatch] = useState({ key: listKey, limit: FIRST_BATCH });
    const limit = batch.key === listKey ? batch.limit : FIRST_BATCH;
    const virtualRef = useRef(false);
    useEffect(() => {
        if (virtualRef.current || limit >= shown.length) return;
        const id = requestAnimationFrame(() => setBatch({ key: listKey, limit: limit + BATCH }));
        return () => cancelAnimationFrame(id);
    }, [listKey, limit, shown.length]);
    const current = collections.find((c) => c.name === selected);
    const filtered = !!q || f.wV > 0 || f.dV > 0 || f.lV > 0;

    const controller = useScrollController();
    const gridRef = useRef<HTMLDivElement>(null);
    const [view, setView] = useState({ key: listKey, top: 0, height: 0, width: 0, pitchW: 0, pitchH: 0 });
    const measure = useCallback(() => {
        const c = controller?.container;
        const g = gridRef.current;
        if (!c || !g) return;
        const card = g.querySelector(`.${styles.card}`) as HTMLElement | null;
        const hasCard = !!card && card.offsetWidth > 0;
        setView((v) => {
            const next = {
                key: listKey, top: c.scrollTop, height: c.clientHeight, width: g.offsetWidth,
                pitchW: hasCard ? (card!.offsetWidth * CARD_W) / 80 : v.pitchW,
                pitchH: hasCard ? (card!.offsetHeight * CARD_H) / 128 : v.pitchH,
            };
            return v.key === next.key && v.top === next.top && v.height === next.height && v.width === next.width && v.pitchW === next.pitchW && v.pitchH === next.pitchH ? v : next;
        });
    }, [controller, listKey]);
    useEffect(() => {
        const id = requestAnimationFrame(measure);
        return () => cancelAnimationFrame(id);
    });
    useEffect(() => {
        try { controller?.scrollTo(0, 0); } catch { }
    }, [listKey, controller]);
    const top = view.key === listKey ? view.top : 0;
    const virtual = !!controller && view.width > 0 && view.height > 0 && view.pitchH > 0;
    const cols = virtual ? Math.max(1, Math.floor((view.width + 1) / view.pitchW)) : 1;
    const rows = Math.ceil(shown.length / cols);
    const firstRow = virtual ? Math.max(0, Math.min(Math.floor(top / view.pitchH) - SPARE_ROWS, rows - 1 - 2 * SPARE_ROWS)) : 0;
    const lastRow = virtual ? Math.min(rows, Math.ceil((top + view.height) / view.pitchH) + SPARE_ROWS) : rows;
    virtualRef.current = virtual;
    const visible = virtual ? shown.slice(firstRow * cols, lastRow * cols) : shown.slice(0, limit);

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
                {status && status !== "OK" && <div className={styles.warn}>{status}</div>}
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
                <Scrollable className={styles.gridScroll} vertical trackVisibility="scrollable" controller={controller} onScroll={measure}>
                    <div className={styles.grid} ref={gridRef}>
                        {virtual && firstRow > 0 && visible.length > 0 && <div className={styles.gridSpacer} style={{ height: `${firstRow * view.pitchH}px` }} />}
                        {visible.map(({ b, label }) => <Card key={b.id} b={b} label={label} on={b.id === plopping} />)}
                        {virtual && lastRow < rows && <div className={styles.gridSpacer} style={{ height: `${(rows - lastRow) * view.pitchH}px` }} />}
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
                            <div className={styles.footerHint}>
                                <span title="While placing a building from this list, the Reshuffle key (T by default) switches to another random one from the buildings shown here.">Click places it · Reshuffle key: random one</span>
                            </div>
                        </div>
                        <button className={styles.textDanger} disabled={shown.length === 0}
                            onClick={() => filtered ? actions.removeBuildings(shown.map(({ b }) => b.id)) : setEditing("removeAll")}>
                            {filtered ? `Remove ${shown.length} shown` : "Remove all"}
                        </button>
                    </div>
                )}
            </div>
        </div>
    );
};
