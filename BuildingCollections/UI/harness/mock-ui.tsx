import React, { createContext, useContext, useState } from "react";

// Aproximaciones de los componentes de cs2/ui para el navegador. La apariencia real la pone el motor del juego.
export const Button = (p: any) => (
    <button disabled={p.disabled} className={`mock-btn v-${p.variant ?? "default"}${p.selected ? " sel" : ""} ${p.className ?? ""}${p.variant === "floating" ? " gameBtn" : ""}`}
        onClick={() => p.onSelect && p.onSelect()}>
        {p.src && <img src={p.src} />}{p.children}
    </button>
);
export const Tooltip = (p: any) => <span title={typeof p.tooltip === "string" ? p.tooltip : ""}>{p.children}</span>;
export const Panel = (p: any) => (
    <div className={"mock-panel " + (p.className ?? "")}>
        <div className="mock-panel-h">{p.header}<button onClick={p.onClose}>×</button></div>
        <div className="mock-panel-b">{p.children}</div>
    </div>
);
export const PanelSectionRow = (p: any) => <div className="mock-row"><span>{p.left}</span><div>{p.right}</div></div>;
export const Scrollable = (p: any) => <div className={p.className} style={{ overflowY: "auto" }}>{p.children}</div>;

// Aproximación del Dropdown del juego (la apariencia real la pone el motor): toggle negro con borde azul y menú gris.
const Ctx = createContext<{ open: boolean; setOpen: (v: boolean) => void }>({ open: false, setOpen: () => {} });
export const Dropdown = (p: any) => {
    const [open, setOpen] = useState(false);
    return (
        <Ctx.Provider value={{ open, setOpen }}>
            <div style={{ position: "relative" }}>
                {open && <div style={{ position: "fixed", inset: 0, zIndex: 100 }} onClick={() => setOpen(false)} />}
                {p.children}
                {open && (
                    <div style={{ position: "absolute", left: 0, right: 0, top: "100%", zIndex: 101, maxHeight: 240, overflowY: "auto", background: "#555", border: "2px solid #5aa9c9", color: "#fff", font: "13px system-ui" }}>
                        {p.content}
                    </div>
                )}
            </div>
        </Ctx.Provider>
    );
};
export const DropdownToggle = (p: any) => {
    const { open, setOpen } = useContext(Ctx);
    return (
        <button onClick={() => setOpen(!open)} style={{ width: "100%", textAlign: "left", display: "flex", justifyContent: "space-between", padding: "7px 10px", background: "#000", color: "#fff", border: "2px solid #5aa9c9", font: "13px system-ui" }}>
            <span>{p.children}</span><span>{open ? "⌃" : "⌄"}</span>
        </button>
    );
};
export const DropdownItem = (p: any) => {
    const { setOpen } = useContext(Ctx);
    return (
        <div onClick={() => { p.onChange && p.onChange(p.value); if (p.closeOnSelect) setOpen(false); }}
            style={{ padding: "7px 10px", background: p.selected ? "#6a6a6a" : "transparent", cursor: "pointer" }}>{p.children}</div>
    );
};
