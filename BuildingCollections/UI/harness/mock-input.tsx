import React from "react";

// En el navegador no hay acciones de entrada del juego: el consumidor solo pinta a sus hijos.
export const InputActionConsumer = (p: { children?: React.ReactNode }) => <>{p.children}</>;
