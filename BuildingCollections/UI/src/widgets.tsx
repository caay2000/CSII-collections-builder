import React, { useEffect, useRef, useState } from "react";
import styles from "./panel.module.scss";
import { CollectionDto } from "./bindings";

/** Iconos .svg servidos desde la carpeta del mod (Mod.cs registra coui://buildingcollections/). */
export const icon = (name: string) => `coui://buildingcollections/icons/${name}.svg`;

export const IconButton = (p: { name: string; title: string; className?: string; disabled?: boolean; onClick: () => void }) => (
    <button className={p.className ?? styles.iconBtn} title={p.title} disabled={p.disabled} onClick={p.onClick}>
        <img src={icon(p.name)} />
    </button>
);

// Esc lo recibe el panel (acción "Back"). Si hay un campo en línea abierto, Esc lo cancela en vez de cerrar el panel.
// El campo también ve la tecla Esc (keydown) y puede desmontarse antes de que llegue "Back": esa misma pulsación
// no debe cerrar además el panel.
let escapeOverride: (() => void) | null = null;
let lastEscape = 0;

/**
 * Único punto de entrada de Esc (tecla leída desde C#, acción "Back" o el propio campo): una misma pulsación
 * puede llegar por varios caminos y solo cuenta una vez. Cancela el campo en línea abierto o, si no hay, cierra.
 */
export const onEscape = (close: () => void) => {
    const now = Date.now();
    if (now - lastEscape < 400) return;
    lastEscape = now;
    if (escapeOverride) escapeOverride();
    else close();
};

/** Campo en línea: Enter confirma, Esc cancela. Se enfoca al aparecer. */
export const InlineInput = (p: { initial?: string; placeholder: string; onSubmit: (v: string) => void; onCancel: () => void }) => {
    const [value, setValue] = useState(p.initial ?? "");
    const ref = useRef<HTMLInputElement>(null);
    useEffect(() => {
        ref.current?.focus();
        escapeOverride = p.onCancel;
        return () => { if (escapeOverride === p.onCancel) escapeOverride = null; };
    }, [p.onCancel]);
    return (
        <input ref={ref} className={styles.inlineInput} value={value} placeholder={p.placeholder}
            onChange={(e) => setValue(e.target.value)}
            onKeyDown={(e) => {
                if (e.key === "Enter" || e.keyCode === 13) { if (value.trim()) p.onSubmit(value.trim()); }
                else if (e.key === "Escape" || e.keyCode === 27) onEscape(() => undefined);
            }} />
    );
};

export const OPS = ["=", "≠", ">", "<"];

/** Control de flechas: [op] ∨ valor ∧. El operador conserva su hueco aunque no se vea, para que nada se mueva. */
export const StepFilter = (p: {
    label: string; op: number; value: number; max?: number; onChange: (op: number, value: number) => void;
}) => {
    const max = p.max ?? 5;
    return (
        <div className={styles.filter}>
            <span className={styles.filterLabel}>{p.label}</span>
            <button className={p.value > 0 ? styles.stepOp : `${styles.stepOp} ${styles.stepOpHidden}`}
                title="Comparison (click to cycle = ≠ > <)" onClick={() => p.onChange((p.op + 1) % OPS.length, p.value)}>{OPS[p.op]}</button>
            <Stepper value={p.value === 0 ? "All" : `${p.value}`}
                onDown={() => p.onChange(p.op, Math.max(0, p.value - 1))}
                onUp={() => p.onChange(p.op, Math.min(max, p.value + 1))} />
        </div>
    );
};

/** ∨ valor ∧ */
export const Stepper = (p: { value: string; onDown: () => void; onUp: () => void }) => (
    <>
        <button className={styles.stepBtn} title="Decrease" onClick={p.onDown}><img src={icon("chevron-down")} /></button>
        <span className={styles.stepValue}>{p.value}</span>
        <button className={styles.stepBtn} title="Increase" onClick={p.onUp}><img src={icon("chevron-up")} /></button>
    </>
);

/** Fila con casilla a la derecha, alineada con el valor de los filtros. */
export const CheckRow = (p: { label: string; checked: boolean; title?: string; accent?: boolean; onChange: (v: boolean) => void }) => (
    <div className={styles.filter} title={p.title}>
        <span className={p.accent ? styles.filterLabelAnarchy : styles.filterLabel}>{p.label}</span>
        <button className={p.checked ? `${styles.check} ${styles.checkOn}` : styles.check} onClick={() => p.onChange(!p.checked)}>
            {p.checked && <img src={icon("check")} />}
        </button>
    </div>
);

/** Desplegable propio con el estilo del panel (el nativo del juego salía blanco y fuera de tono). */
export const CollectionPicker = (p: { items: CollectionDto[]; selected: string; onSelect: (n: string) => void; noneLabel?: string }) => {
    const [open, setOpen] = useState(false);
    const cur = p.items.find((c) => c.name === p.selected);
    return (
        <div className={styles.dd}>
            <button className={styles.ddToggle} onClick={() => setOpen(!open)}>
                <span className={styles.ddLabel}>{cur ? cur.name : p.noneLabel ?? "Choose a collection"}</span>
                {cur && <span className={styles.ddCount}>{`${cur.count}`}</span>}
                <img src={icon(open ? "chevron-up" : "dropdown")} />
            </button>
            {open && (
                <div className={styles.ddList}>
                    {p.noneLabel && (
                        <div className={!cur ? styles.itemOn : styles.item} onClick={() => { p.onSelect(""); setOpen(false); }}>
                            <span className={styles.itemName}>{p.noneLabel}</span>
                        </div>
                    )}
                    {p.items.length === 0 && <div className={styles.empty}>No collections yet.</div>}
                    {p.items.map((c) => (
                        <div key={c.name} className={c.name === p.selected ? styles.itemOn : styles.item}
                            onClick={() => { p.onSelect(c.name); setOpen(false); }}>
                            <span className={styles.itemName}>{c.name}</span><span className={styles.itemCount}>{`${c.count}`}</span>
                        </div>
                    ))}
                </div>
            )}
        </div>
    );
};
