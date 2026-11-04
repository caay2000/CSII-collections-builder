// En el juego "1rem" equivale a un píxel de la UI escalada; aquí se mapea a una variable para poder hacer zoom.
module.exports = function (source) {
    return source.replace(/(-?\d*\.?\d+)rem/g, "calc($1 * var(--u))");
};
