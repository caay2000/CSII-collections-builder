const path = require("path");
const MOD = require("./mod.json");
const MiniCssExtractPlugin = require("mini-css-extract-plugin");

const OUTPUT_DIR = process.env.BC_OUT ||
  "/mnt/c/Users/caay2/AppData/LocalLow/Colossal Order/Cities Skylines II/Mods/" + MOD.id;

module.exports = {
  mode: "production",
  entry: { [MOD.id]: "./src/index.tsx" },
  devtool: false,
  externalsType: "window",
  externals: {
    react: "React", "react-dom": "ReactDOM",
    "cs2/modding": "cs2/modding", "cs2/api": "cs2/api", "cs2/bindings": "cs2/bindings",
    "cs2/l10n": "cs2/l10n", "cs2/ui": "cs2/ui", "cs2/input": "cs2/input", "cs2/utils": "cs2/utils",
    "cohtml/cohtml": "cohtml/cohtml",
  },
  module: {
    rules: [
      { test: /\.tsx?$/, use: "ts-loader", exclude: /node_modules/ },
      {
        test: /\.s?css$/, include: path.join(__dirname, "src"),
        use: [
          MiniCssExtractPlugin.loader,
          { loader: "css-loader", options: { url: false, importLoaders: 1, modules: { auto: true, exportLocalsConvention: "camelCase", localIdentName: "[local]_[hash:base64:3]" } } },
          { loader: "sass-loader", options: { sassOptions: { quietDeps: true } } },
        ],
      },
    ],
  },
  resolve: { extensions: [".tsx", ".ts", ".js"], modules: ["node_modules", path.join(__dirname, "src")] },
  output: { path: OUTPUT_DIR, library: { type: "module" }, publicPath: "coui://ui-mods/" },
  optimization: { minimize: false },
  experiments: { outputModule: true },
  plugins: [
    new MiniCssExtractPlugin(),
    {
      // El juego pide que el módulo exporte si trae CSS.
      apply(compiler) {
        compiler.hooks.compilation.tap("CSSPresence", (compilation) => {
          compilation.hooks.processAssets.tap({ name: "CSSPresence", stage: compilation.PROCESS_ASSETS_STAGE_ADDITIONS }, () => {
            const hasCSS = Object.keys(compilation.assets).some((a) => a.endsWith(".css"));
            for (const chunk of compilation.chunks) for (const file of chunk.files) {
              if (!file.endsWith(".mjs")) continue;
              const src = compilation.getAsset(file).source.source().toString();
              compilation.updateAsset(file, new compiler.webpack.sources.RawSource(
                src.replace("export {", `const hasCSS = ${hasCSS}; export { hasCSS, `)));
            }
          });
        });
      },
    },
  ],
};
