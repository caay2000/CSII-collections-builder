import { useSyncExternalStore } from "react";
import { read, subscribe, trigger as t } from "./backend";

export function bindValue<T>(group: string, name: string, fallback?: T) {
    return { group, name, fallback, get value(): T { return read(name) as unknown as T; } };
}
export function useValue<T>(binding: { name: string }): T {
    return useSyncExternalStore(subscribe, () => read(binding.name)) as unknown as T;
}
export function trigger(_group: string, name: string, ...args: any[]) { t(name, ...args); }
