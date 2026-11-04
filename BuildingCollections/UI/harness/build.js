const fs = require("fs"), path = require("path");
const js = fs.readFileSync(path.join(__dirname, "dist", "harness.js"), "utf8").replace(/<\/script/gi, "<\\/script");
const css = fs.readFileSync(path.join(__dirname, "harness.css"), "utf8");
// Iconos reales del mod (coui://buildingcollections/icons/X.svg) como data URIs, para que el arnés se parezca al juego.
const iconDir = path.join(__dirname, "..", "icons");
const icons = Object.fromEntries(fs.readdirSync(iconDir).filter((f) => f.endsWith(".svg"))
    .map((f) => [f.replace(".svg", ""), "data:image/svg+xml;utf8," + encodeURIComponent(fs.readFileSync(path.join(iconDir, f), "utf8"))]));
const html = `<title>Building Collections Harness</title>
<script>window.BC_ICONS=${JSON.stringify(icons)};</script>
<style>${css}</style>
<div id="root"></div>
<script>${js}</script>`;
fs.writeFileSync(path.join(__dirname, "dist", "harness.html"), html);
console.log("harness.html", (html.length / 1024).toFixed(0) + " KB");
