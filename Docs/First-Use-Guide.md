# BOM authoring

Open **BOM > Authoring workspace**. For an existing example, open `Assets/BOM/Fixtures/ReferenceLevel.unity`.

## Import

1. Drop a Blender-exported FBX into **Model (FBX)**. Choose **Front**, **Top**, or **Side** from the silhouette previews. An outlined card suggests a usable view when the current projection is empty.
2. Compare **Source + outline** with **Result**. Click an outline point in either view to select it. Right-drag the model preview to orbit; drag points in the flat outline to edit. Double-click an edge to insert a point. Delete removes the selected point; Escape cancels a drag. Undo/Redo work normally.
3. Check the dimensions and scale bar. Width and height are in metres; proportions are locked initially. Imported root unit scaling is preserved. Thickness is visual; mass comes from solid area and the selected density.
4. Choose a **Physics material** and, for destructible pieces, a **Response**. Fresh imports use **Default** and **Standard**, or your last saved choices. Use **Presets** for other starters and **Duplicate** to customize a copy. **Shared** means inline edits are saved immediately and affect every use, including when piece edits are discarded.
5. Adjust **Appearance**. **Import appearance** copies the selected Unity material's base colour and texture; the result uses BOM's extrusion shader. Arbitrary source shaders, UV layouts and multiple material slots are not reproduced by the extrusion.
6. Use **Save piece** or **Save and place**. The save name follows the source model; the output folder and saved material/response selections are remembered.

Click a disconnected region and use **Keep selected region** to retain it. Only one connected region can be saved as one piece. **Rebuild outline** requires confirmation before replacing manual corrections. Exact coordinates, source conversion and texture mapping remain under **Advanced**.

## Starter presets

The library is in `Assets/BOM/Presets`, with subfolders for materials, responses and explosions. Supplied starters are read-only in the authoring window; duplicate one to edit it. **BOM > Create missing starter presets** restores missing assets without overwriting existing ones. Verification examples remain in `Assets/BOM/Fixtures`.

| Material | Density (kg/m²) | Blast resistance |
| --- | ---: | ---: |
| Default | 2 | 1 |
| Light | 1 | 1 |
| Heavy | 4 | 1 |
| Tough | 2 | 3 |

All four use friction 0.5, no bounce, and linear/angular damping 0.1. These are gameplay choices rather than physical wood or metal specifications. Density changes mass; blast resistance changes penetration. **Standard** uses the explosion's added speed; **No push** still destroys material but adds no speed. Density does not change the added speed.

| Explosion | Max radius (m) | Reach budget | Added speed (m/s) |
| --- | ---: | ---: | ---: |
| Small | 0.8 | 1.6 | 0.3 |
| Medium | 2 | 4 | 1 |
| Large | 4 | 8 | 2 |

Air spends one reach unit per metre; material adds its resistance to that cost. Radius is a maximum. Actual damage depends on blast position, shape and shielding; a larger blast does not guarantee splitting or removal.

## Place and connect

**Save and place** starts a scene placement preview, creating a level group if needed. Click to place; Escape cancels placement. **Place at view centre** places immediately and frames the piece. The preview shows its scene size against the grid.

Move in XY and rotate about Z. A selected piece's scale handle changes X/Y and keeps Z at 1. When scaling parent groups, keep Z at 1 and avoid shear or reflection. The placement Inspector shows resulting dimensions and mass, along with independent material, destruction and grab overrides.

Select placements to mark fixed supports. Select two touching pieces in **Connect**, propose their shared boundary, and confirm the highlighted attachment. Alignment remains a preview until confirmation.

## Playtest

New level groups include **Medium**. Use **Presets** to choose another blast, then add it to the level. Select a configured preset to see its values; **Duplicate** uses an editable copy in this level. Validate the level, then enter Play mode. Shift-click exterior air in Scene view or enter an XY origin and use **Run explosion**. **Reset to last captured start** restores the frozen start; starting again captures current authoring values. Source assets are preserved. Origins inside solids or enclosed cavities are unsupported.
