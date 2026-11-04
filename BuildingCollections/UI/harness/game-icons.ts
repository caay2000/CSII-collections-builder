// Aproximaciones de los iconos del juego (coui://uil/... y Media/...) que el navegador no puede cargar.
const svg = (body: string) => "data:image/svg+xml;utf8," + encodeURIComponent(
    `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" fill="#fff" stroke="#fff" stroke-linecap="round" stroke-linejoin="round">${body}</svg>`);

export const GAME_ICONS: Record<string, string> = {
    StarAll: svg('<path d="M12 3v18M4.2 7.5l15.6 9M4.2 16.5l15.6-9" stroke-width="2.6" fill="none"/>'),
    TreesDeciduous: svg('<ellipse cx="8" cy="8" rx="4" ry="5.5" stroke="none"/><ellipse cx="16.5" cy="9.5" rx="3.5" ry="5" stroke="none"/><path d="M8 13v8M16.5 14v7" stroke-width="1.8" fill="none"/>'),
    TreesNeedle: svg('<path d="M7 2l-4.5 13h9zM16.5 5l-4 11h8z" stroke="none"/><path d="M7 15v6M16.5 16v5" stroke-width="1.8" fill="none"/>'),
    Bushes: svg('<circle cx="7" cy="16" r="3.4" stroke="none"/><circle cx="17" cy="16" r="3.4" stroke="none"/><circle cx="12" cy="9" r="3.4" stroke="none"/>'),
    TreeChild: svg('<path d="M12 21v-8" stroke-width="1.8" fill="none"/><path d="M12 14c-1-4-4.5-6-8-5.5 0 3.5 3.5 6 8 5.5zM12 12c1-3.5 4-5 7-4.5 0 3-3 5-7 4.5z" stroke="none"/>'),
    TreeTeen: svg('<path d="M12 21v-9M12 15l-3-2.5M12 14l3-2.5" stroke-width="1.8" fill="none"/><circle cx="8" cy="9" r="3" stroke="none"/><circle cx="16" cy="9" r="3" stroke="none"/><circle cx="12" cy="6" r="3.2" stroke="none"/>'),
    TreeAdult: svg('<path d="M12 22v-5" stroke-width="2.2" fill="none"/><circle cx="12" cy="10" r="8" stroke="none"/>'),
    TreeElderly: svg('<path d="M11 22v-6l-3-2M13 22v-6l3-3" stroke-width="2.2" fill="none"/><path d="M5 9a4 4 0 0 1 5-5 4 4 0 0 1 6 0 4 4 0 0 1 3 6 3.5 3.5 0 0 1-4 4H9a4 4 0 0 1-4-5z" stroke="none"/>'),
};
