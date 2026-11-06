import React, { useEffect, useMemo, useRef, useState } from "react";
import { Button, Tooltip } from "cs2/ui";
import { InputActionConsumer } from "cs2/input";
import styles from "./panel.module.scss";
import { actions, useEscapes, usePanelOpen } from "./bindings";
import { CollectionsTab } from "./collectionsTab";
import { BuilderTab } from "./builderTab";
import { icon, onEscape } from "./widgets";

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

let lastTab: Tab = "collections";
let offset = { x: 0, y: 0 };

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
    const back = useMemo(() => ({ Back: () => onEscape(() => actions.setPanelOpen(false)) }), []);
    const escapes = useEscapes();
    const seenEscapes = useRef(escapes);
    useEffect(() => {
        if (escapes !== seenEscapes.current) {
            seenEscapes.current = escapes;
            onEscape(() => actions.setPanelOpen(false));
        }
    }, [escapes]);
    return (
        <InputActionConsumer actions={back} ignoreFocusState>
            <div className={tab === "collections" ? styles.panelWide : styles.panel} style={drag.style}>
                <div className={styles.header} onMouseDown={drag.start}>
                    <span className={styles.title}>Building Collections</span>
                    <button className={styles.close} title="Close" onClick={() => actions.setPanelOpen(false)}><img src={icon("close")} /></button>
                </div>
                <div className={styles.tabs}>
                    <button className={tab === "collections" ? styles.tabOn : styles.tab} onClick={() => setTab("collections")}>Collections</button>
                    <button className={tab === "builder" ? styles.tabOn : styles.tab} onClick={() => setTab("builder")}>Builder</button>
                </div>
                <div className={styles.body}>{tab === "collections" ? <CollectionsTab /> : <BuilderTab />}</div>
            </div>
        </InputActionConsumer>
    );
};
