const fs = require("fs"), path = require("path");
const js = fs.readFileSync(path.join(__dirname, "dist", "harness.js"), "utf8").replace(/<\/script/gi, "<\\/script");
const css = fs.readFileSync(path.join(__dirname, "harness.css"), "utf8");
const html = `<title>Building Collections Harness</title>
<style>${css}</style>
<div id="root"></div>
<script>${js}</script>`;
fs.writeFileSync(path.join(__dirname, "dist", "harness.html"), html);
console.log("harness.html", (html.length / 1024).toFixed(0) + " KB");
