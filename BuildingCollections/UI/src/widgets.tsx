import React, { useState } from "react";
import * as ui from "cs2/ui";
import styles from "./panel.module.scss";
import { CollectionDto } from "./bindings";

// Los tipos de cs2/ui declaran DropdownItem solo como interfaz; en ejecución es un componente.
const Dropdown = ui.Dropdown;
const DropdownToggle = ui.DropdownToggle;
const DropdownItem: React.ComponentType<any> = (ui as any).DropdownItem;

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
export const Slider = (p: { min: number; max: number; value: number; onChange: (v: number) => void }) => {
    const [drag, setDrag] = useState(false);
    const pct = ((p.value - p.min) / (p.max - p.min)) * 100;
    const set = (e: React.MouseEvent<HTMLDivElement>) => {
        const r = e.currentTarget.getBoundingClientRect();
        const t = Math.min(1, Math.max(0, (e.clientX - r.left) / r.width));
        p.onChange(Math.round(p.min + t * (p.max - p.min)));
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

/** Desplegable nativo del juego (el mismo de los ajustes): flota sobre el panel, hace scroll y se cierra solo. */
export const CollectionPicker = (p: { items: CollectionDto[]; selected: string; onSelect: (n: string) => void }) => {
    const cur = p.items.find((c) => c.name === p.selected);
    return (
        <Dropdown
            content={
                <>
                    {p.items.map((c) => (
                        <DropdownItem key={c.name} value={c.name} selected={c.name === p.selected} closeOnSelect onChange={p.onSelect}>
                            {c.name} ({c.count})
                        </DropdownItem>
                    ))}
                </>
            }>
            <DropdownToggle>{cur ? `${cur.name} (${cur.count})` : "Choose a collection"}</DropdownToggle>
        </Dropdown>
    );
};
