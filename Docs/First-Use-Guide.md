# BOM authoring

Open **BOM > Authoring workspace**. For an existing example, open `Assets/BOM/Fixtures/ReferenceLevel.unity`.

## Import

1. Drop a Blender-exported FBX into **Model (FBX)**. Choose **Front**, **Top**, or **Side** from the silhouette previews. An outlined card suggests a usable view when the current projection is empty.
2. Compare **Source + outline** with **Result**. Click an outline point in either view to select it. Right-drag the model preview to orbit; drag points in the flat outline to edit. Double-click an edge to insert a point. Delete removes the selected point; Escape cancels a drag. Undo/Redo work normally.
3. Check the dimensions and scale bar. Width and height are in metres; proportions are locked initially. Imported root unit scaling is preserved. Thickness is visual; mass comes from solid area and the selected density.
4. Choose a **Physics material** and, for destructible pieces, a **Response**. Their properties are editable inline. These are shared assets, so edits affect every piece using them.
5. Adjust **Appearance**. **Import appearance** copies the selected Unity material's base colour and texture; the result uses BOM's extrusion shader. Arbitrary source shaders, UV layouts and multiple material slots are not reproduced by the extrusion.
6. Use **Save piece** or **Save and place**. The save name follows the source model; the output folder and saved material/response selections are remembered.

Click a disconnected region and use **Keep selected region** to retain it. Only one connected region can be saved as one piece. **Rebuild outline** requires confirmation before replacing manual corrections. Exact coordinates, source conversion and texture mapping remain under **Advanced**.

## Place and connect

**Save and place** starts a scene placement preview, creating a level group if needed. Click to place; Escape cancels placement. **Place at view centre** places immediately and frames the piece. The preview shows its scene size against the grid.

Move in XY and rotate about Z. A selected piece's scale handle changes X/Y and keeps Z at 1. When scaling parent groups, keep Z at 1 and avoid shear or reflection. The placement Inspector shows resulting dimensions and mass, along with independent material, destruction and grab overrides.

Select placements to mark fixed supports. Select two touching pieces in **Connect**, propose their shared boundary, and confirm the highlighted attachment. Alignment remains a preview until confirmation.

## Playtest

Add or create a named explosion preset, validate the level, then enter Play mode. Shift-click exterior air in Scene view or enter an XY origin and use **Run explosion**. **Reset to last captured start** restores the frozen start; starting again captures current authoring values. Source assets are preserved. Origins inside solids or enclosed cavities are unsupported.
