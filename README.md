# CSII-collections-builder

Mod for Cities: Skylines II (`BuildingCollections`):

- **Collections**: named lists of buildings, filled from Find It's current filter ("Add all Find It items") or
  with Shift+click on a building in Find It or the game's asset menu. Every collection is also a Find It tag.
- **Block builder**: click a city block surrounded by roads, choose a collection, width/depth/level filters and
  density, preview a random combination and build it. Corner lots get corner buildings when the collection has
  them. Growables placed this way are protected from being condemned.

Find It is optional (only used for "Add all" and tags, via reflection). No other mod is required.

## Layout

| Path | What |
|---|---|
| `BuildingCollections/` | The mod (C#, net48). `Core/` is pure logic with no game dependencies. |
| `BuildingCollections/UI/` | Panel in React/TypeScript (webpack). `harness/` runs it in a browser with a simulated backend. |
| `BuildingCollections.Tests/` | xUnit tests for `Core/`. |
| `docs/` | Feature spec, implementation rules and investigation notes (Spanish). |
| `tools/` | Helper scripts (startup log report). |

## Build and develop

```bash
dotnet test BuildingCollections.Tests                     # pure logic
dotnet build BuildingCollections -p:Deploy=true           # DLL + icon to the local Mods folder (game closed)
BuildingCollections/UI/deploy-ui.sh                       # UI to the local Mods folder
```

Paths default to a WSL setup; override `ManagedPath` / `LocalModsPath` (csproj) and `BC_OUT` (UI).

Fewer game restarts:

- Launch the game with `--uiDeveloperMode`: UI changes reload live (`deploy-ui.sh` touches the `.css`, because
  the game does not reload on `.mjs` changes) and the UI debugger is at `http://localhost:9444`.
- `ModsData/BuildingCollections/dev.json` holds tunable placement parameters, re-read on every action.
- A development playset with only this mod and Find It starts much faster.

## Credits

- Webpack/module setup follows the pattern of [Network Tools](https://github.com/lucarager/CS2-NetworkTools)
  by lucarager (MIT).
- The protection against `Condemned` follows the approach of
  [Plop the Growables](https://github.com/algernon-A/PlopTheGrowables) by algernon (Apache-2.0); no code copied.
- Find It by T.D.W. is used through reflection only.

MIT License.
