import { useEffect } from "react";
import { actions } from "./bindings";

const GRID_ITEM = "gridItem_";
const SELECTED = "selected_";

function selectedFindItItem(target: EventTarget | null): boolean {
    for (let el = target as Element | null; el; el = el.parentElement) {
        const classes = (el.getAttribute?.("class") ?? "").split(/\s+/);
        if (classes.some(c => c.startsWith(GRID_ITEM))) {
            return classes.some(c => c.startsWith(SELECTED));
        }
    }
    return false;
}

export function useFindItReselect(enabled: boolean) {
    useEffect(() => {
        if (!enabled) return;
        const click = (e: MouseEvent) => {
            if (e.shiftKey && e.button === 0 && selectedFindItItem(e.target)) actions.addActivePrefab();
        };
        document.addEventListener("click", click, true);
        return () => document.removeEventListener("click", click, true);
    }, [enabled]);
}
