import React, { useState } from "react";
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

const Panel = () => {
    const [tab, setTab] = useState<Tab>("collections");
    return (
        <div className={tab === "collections" ? styles.panelWide : styles.panel}>
            <div className={styles.header}>
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
