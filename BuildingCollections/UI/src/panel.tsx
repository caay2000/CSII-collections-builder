import React, { useEffect, useRef, useState } from "react";
import { Button, Tooltip } from "cs2/ui";
import styles from "./panel.module.scss";
import { actions, usePanelOpen } from "./bindings";
import { CollectionsTab } from "./collectionsTab";
import { BuilderTab } from "./builderTab";

// Servido desde la carpeta del mod (Mod.cs registra coui://buildingcollections/). El motor no admite data: URIs.
const ICON = "coui://buildingcollections/icon.svg";

export const GameTopLeftButton = () => {
    const open = usePanelOpen();
    return (
        <>
            <Tooltip tooltip="Building Collections" direction="down" delayTime={0}>
                <Button variant="floating" src={ICON} selected={open} onSelect={() => actions.setPanelOpen(!open)} />
            </Tooltip>
            {open && <Panel />}
        </>
    );
};

type Tab = "collections" | "builder";

// Última pestaña usada y posición del panel: se conservan al cerrarlo y abrirlo (mientras el juego esté abierto).
let lastTab: Tab = "collections";
let offset = { x: 0, y: 0 };

/** Arrastre por la cabecera: desplaza el panel con transform, desde su sitio por defecto. */
function useDrag() {
    const [pos, setPos] = useState(offset);
    const drag = useRef<{ mx: number; my: number; x: number; y: number } | null>(null);
    useEffect(() => {
        const move = (e: MouseEvent) => {
            if (!drag.current) return;
            const d = drag.current;
            offset = { x: d.x + e.clientX - d.mx, y: d.y + e.clientY - d.my };
            setPos(offset);
        };
        const up = () => { drag.current = null; };
        window.addEventListener("mousemove", move);
        window.addEventListener("mouseup", up);
        return () => { window.removeEventListener("mousemove", move); window.removeEventListener("mouseup", up); };
    }, []);
    const start = (e: React.MouseEvent) => { drag.current = { mx: e.clientX, my: e.clientY, x: pos.x, y: pos.y }; };
    return { style: { transform: `translate(${pos.x}px, ${pos.y}px)` }, start };
}

const Panel = () => {
    const [tab, setTabState] = useState<Tab>(lastTab);
    const setTab = (t: Tab) => { lastTab = t; setTabState(t); };
    const drag = useDrag();
    return (
        <div className={tab === "collections" ? styles.panelWide : styles.panel} style={drag.style}>
            <div className={styles.header} onMouseDown={drag.start}>
                <span className={styles.title}>Building Collections</span>
                <button className={styles.close} onClick={() => actions.setPanelOpen(false)}>×</button>
            </div>
            <div className={styles.tabs}>
                <button className={tab === "collections" ? styles.tabOn : styles.tab} onClick={() => setTab("collections")}>Collections</button>
                <button className={tab === "builder" ? styles.tabOn : styles.tab} onClick={() => setTab("builder")}>Builder</button>
            </div>
            <div className={styles.body}>{tab === "collections" ? <CollectionsTab /> : <BuilderTab />}</div>
        </div>
    );
};
