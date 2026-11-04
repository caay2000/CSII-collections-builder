const path = require("path");
module.exports = {
    mode: "production",
    entry: path.join(__dirname, "main.tsx"),
    devtool: false,
    output: { path: path.join(__dirname, "dist"), filename: "harness.js" },
    resolve: {
        extensions: [".tsx", ".ts", ".js"],
        alias: {
            "cs2/api": path.join(__dirname, "mock-api.ts"),
            "cs2/ui": path.join(__dirname, "mock-ui.tsx"),
            "cs2/modding": path.join(__dirname, "mock-modding.ts"),
        },
        modules: [path.join(__dirname, "..", "node_modules")],
    },
    resolveLoader: { modules: [path.join(__dirname, "..", "node_modules"), __dirname] },
    module: {
        rules: [
            { test: /\.tsx?$/, loader: "ts-loader", options: { transpileOnly: true, configFile: path.join(__dirname, "..", "tsconfig.json") }, exclude: /node_modules/ },
            {
                test: /\.s?css$/, include: path.join(__dirname, "..", "src"),
                use: [
                    "style-loader",
                    { loader: "css-loader", options: { url: false, importLoaders: 2, modules: { auto: true, exportLocalsConvention: "camelCase", localIdentName: "[local]_[hash:base64:3]" } } },
                    "rem-loader.js",
                    { loader: "sass-loader", options: { sassOptions: { quietDeps: true } } },
                ],
            },
        ],
    },
    performance: { hints: false },
};
