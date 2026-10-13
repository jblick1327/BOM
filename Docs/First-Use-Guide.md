# BOM authoring

Open **BOM > Authoring workspace**. See the [README](../README.md) for project setup and an example level.

## Import

1. Import a static FBX into Unity, then drop it into **Model (FBX)**. Choose **Front**, **Top**, or **Side** from the silhouette previews.
2. Compare **Source + outline** with **Result**. Click points in either view to select; edit in the flat outline. Right-drag the model preview to orbit.
3. Check dimensions and the scale bar. Width/height are in metres; proportions start locked. Imported root unit scaling is preserved. Gameplay runs in XY; thickness is visual. Mass = solid area × density.
4. Choose **Physics material** and **Response**: initially **Default** and **Standard**, then your last saved choices. Use **Presets** to switch.
5. **Import appearance** copies a Unity material's base colour and texture into BOM's extrusion shader. Source shaders, UV layouts and multiple material slots are not reproduced.
6. Resolve validation messages, then **Save piece** or **Save and place**. The output folder and material/response choices are remembered. Choose a saved **Piece** to edit it.

Drag points to edit; double-click an edge to add a point; Delete removes it. Scroll to zoom, middle-drag to pan, or **Fit**. **Add hole** creates an opening. Escape cancels a drag; Undo/Redo are supported.

For disconnected outlines, click a region and **Keep selected region**; one connected region is allowed per piece. **Rebuild outline** confirms before replacing manual edits. Source conversion and texture mapping are under **Advanced**.

## Starter presets

`Assets/BOM/Presets` contains materials, responses and explosions; `Assets/BOM/Fixtures` contains verification examples. Starters are read-only in the authoring window: **Duplicate** to customize. Inline **Shared** edits save immediately and affect every use; discarding piece edits does not undo them. **BOM > Create missing starter presets** restores missing starters without overwriting existing assets.

| Material | Density (kg/m²) | Blast resistance |
| --- | ---: | ---: |
| Default | 2 | 1 |
| Light | 1 | 1 |
| Heavy | 4 | 1 |
| Tough | 2 | 3 |

All use friction 0.5, no bounce, and linear/angular damping 0.1. These are gameplay values. Density changes mass; resistance reduces penetration. **Standard** applies blast push; **No push** destroys without adding speed. Added speed is multiplied by the response and piece's **Push multiplier**, independent of density.

| Explosion | Max radius (m) | Reach budget | Added speed (m/s) |
| --- | ---: | ---: | ---: |
| Small | 0.8 | 1.6 | 0.3 |
| Medium | 2 | 4 | 1 |
| Large | 4 | 8 | 2 |

Reach costs 1 unit/m in air and 1 + resistance in solids. Radius caps reach; position, shape and shielding determine damage.

## Place and connect

**Save and place** previews scene size against the grid, creating a level group if needed. Click to place; Escape cancels. In **Place**, **Place in scene** also previews; **Place at view centre** places immediately and frames the piece.

Move in XY and rotate about Z. Use positive X/Y scales; keep Z scale = 1, including parent groups, and avoid shear. The placement Inspector shows scene dimensions, mass and overrides. Scaling changes area and mass.

Mark floors or anchors as fixed supports. Select two pieces with touching edges in **Connect**, propose their shared boundary, and confirm the highlighted attachment. Alignment is a preview until confirmed.

## Playtest

New level groups include **Medium**. Choose blasts through **Presets** and **Add selected preset to level**. Select one to inspect its values; **Duplicate** makes this level use an editable copy.

**Validate full level**, then **Enter Play mode**. Choose a captured preset and Shift-click exterior air in Scene view, or enter an XY origin and **Run explosion**. Origins inside solids or enclosed holes are unsupported.

**Reset to last captured start** restores the captured level. Stop Play mode to edit; restart to capture changes. Saved pieces are preserved.
