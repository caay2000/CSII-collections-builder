import React, { useState } from "react";
import styles from "./panel.module.scss";
import { CollectionDto } from "./bindings";

export const OPS = ["=", "≠", ">", "<"];

/** Selector de valor como el de Find It: [op] ∨ All ∧ (el operador solo aparece con un valor elegido). */
export const StepFilter = (p: {
    label: string; op: number; value: number; max?: number; onChange: (op: number, value: number) => void;
}) => {
    const max = p.max ?? 5;
    return (
        <div className={styles.row}>
            <span className={styles.label}>{p.label}</span>
            <div className={styles.step}>
                {p.value > 0 && (
                    <button className={styles.stepOp} title="Comparison" onClick={() => p.onChange((p.op + 1) % OPS.length, p.value)}>{OPS[p.op]}</button>
                )}
                <button className={styles.stepArrow} onClick={() => p.onChange(p.op, Math.max(0, p.value - 1))}>∨</button>
                <span className={styles.stepValue}>{p.value === 0 ? "All" : p.value}</span>
                <button className={styles.stepArrow} onClick={() => p.onChange(p.op, Math.min(max, p.value + 1))}>∧</button>
            </div>
        </div>
    );
};

/** Deslizador propio: el motor de la UI no soporta <input type="range">. */
export const Slider = (p: { min: number; max: number; step?: number; value: number; onChange: (v: number) => void }) => {
    const [drag, setDrag] = useState(false);
    const pct = ((p.value - p.min) / (p.max - p.min)) * 100;
    const set = (e: React.MouseEvent<HTMLDivElement>) => {
        const r = e.currentTarget.getBoundingClientRect();
        const t = Math.min(1, Math.max(0, (e.clientX - r.left) / r.width));
        const step = p.step ?? 1;
        p.onChange(Math.round((p.min + t * (p.max - p.min)) / step) * step);
    };
    return (
        <div className={styles.slider}
            onMouseDown={(e) => { setDrag(true); set(e); }}
            onMouseMove={(e) => drag && set(e)}
            onMouseUp={() => setDrag(false)}
            onMouseLeave={() => setDrag(false)}>
            <div className={styles.sliderTrack}><div className={styles.sliderFill} style={{ width: pct + "%" }} /></div>
            <div className={styles.sliderThumb} style={{ left: pct + "%" }} />
        </div>
    );
};

/** Desplegable propio con el estilo del panel (el nativo del juego salía blanco y fuera de tono). Se abre en línea. */
export const CollectionPicker = (p: { items: CollectionDto[]; selected: string; onSelect: (n: string) => void }) => {
    const [open, setOpen] = useState(false);
    const cur = p.items.find((c) => c.name === p.selected);
    return (
        <div className={styles.dd}>
            <button className={styles.ddToggle} onClick={() => setOpen(!open)}>
                <span className={styles.ddLabel}>{cur ? cur.name : "Choose a collection"}</span>
                <span className={styles.muted}>{cur ? `${cur.count}  ${open ? "∧" : "∨"}` : (open ? "∧" : "∨")}</span>
            </button>
            {open && (
                <div className={styles.ddList}>
                    {p.items.length === 0 && <div className={styles.muted}>No collections yet.</div>}
                    {p.items.map((c) => (
                        <div key={c.name} className={c.name === p.selected ? styles.itemOn : styles.item}
                            onClick={() => { p.onSelect(c.name); setOpen(false); }}>
                            <span>{c.name}</span><span className={styles.muted}>{`${c.count}`}</span>
                        </div>
                    ))}
                </div>
            )}
        </div>
    );
};
