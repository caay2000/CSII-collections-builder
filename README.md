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
| `BuildingCollections.Simulator/` | Runs the placement over synthetic and real game blocks (`Blocks/`) and writes an HTML report. |
| `docs/` | Feature spec, implementation rules and investigation notes (Spanish). |
| `tools/` | Helper scripts (startup log report). |

## Build and develop

```bash
dotnet test BuildingCollections.Tests                     # pure logic
dotnet build BuildingCollections -p:Deploy=true           # DLL + icon to the local Mods folder (game closed)
BuildingCollections/UI/deploy-ui.sh                       # UI to the local Mods folder
dotnet run -c Release --project BuildingCollections.Simulator -- report.html [seed] [name filter]
dotnet run -c Release --project BuildingCollections.Simulator -- regress            # compare with the baseline
dotnet run -c Release --project BuildingCollections.Simulator -- regress --update   # record a new baseline
```

**Placement regression.** `BuildingCollections.Simulator/regression-baseline.json` holds, for every simulator scenario
and overlap, the sums over seeds 1-24 of missed corners, gaps between two buildings, uncovered sidewalk meters, empty
street cells and the largest gap. `regress` (and the `SimulatorRegressionTests` test in `dotnet test`) fails when any
of them gets worse than the noise allowance, per scenario and as a total per overlap. The allowances were calibrated
so that six other sets of 24 seeds pass against the baseline. After a fix that improves things, `regress` lists the
`BETTER` lines: record them with `--update` and commit the baseline together with the fix. `--first-seed N` measures
with another seed set, to check that a change is not just luck. A scenario that is not in the baseline also fails
(`NEW`), so new scenarios get recorded. `SIM_WEDGES=1` lists the wedge gaps that the report does not count as gaps.

The simulator uses the `game` catalog by default (no one-cell-deep plain buildings, as in most collections);
set `SIM_CATALOG=with-2x1` or `SIM_CATALOG=full` to try other assortments, and `SIM_LOG=1` to print the planner log.

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
