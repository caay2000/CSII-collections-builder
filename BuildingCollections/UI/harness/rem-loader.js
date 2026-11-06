module.exports = function (source) {
    return source.replace(/(-?\d*\.?\d+)rem/g, "calc($1 * var(--u))");
};
