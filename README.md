# BOM

Import FBX models as destructible XY pieces with 3D visuals, connect them in Unity, and test explosions.

## Open the project

1. Install **Unity 6000.6.5f1** through Unity Hub.
2. Clone or download this repository.
3. In Unity Hub, choose **Add project from disk** and select the folder containing `Assets`, `Packages` and `ProjectSettings`. Open it and wait for imports to finish.
4. Open `Assets/Scenes/StarterLevel.unity`. Select **BOM Level** in the Hierarchy, then choose **BOM > Authoring workspace**.

StarterLevel is empty. See the [authoring guide](Docs/First-Use-Guide.md) for controls and preset values.

## Optional: try the example

Open `Assets/BOM/Fixtures/ReferenceLevel.unity`, select **BOM Reference Level**, and follow the playtest steps below. Fixtures are verification examples; reusable starters live in `Assets/BOM/Presets`.

## Make an asset

1. Export a static FBX from Blender, import it into Unity, then drop it into **Model (FBX)** under **Import**.
2. Choose **Front**, **Top**, or **Side**. Compare **Source + outline** with **Result**; edit points in the flat outline.
3. Check **Width (m)** and **Height (m)**. Thickness is visual; mass is solid area × density.
4. Choose **Default** for **Physics material** (mass/resistance) and **Standard** for **Response** (blast push). **Presets** switches starters; **Duplicate** makes an editable copy.
5. Under **Appearance**, **Import appearance** copies a Unity material's base colour and texture into BOM's shader.
6. Resolve validation messages, then **Save piece** or **Save and place**. Click in Scene view to place; Escape cancels placement.

Save assets under `Assets`. Select an existing **Piece** to edit. Piece edits require saving; inline **Shared** asset edits save immediately for every use, even after **Discard edits**.

## Build a level

Save a copy of StarterLevel. In **Place**, choose its level group and a **Piece**, then **Place in scene** and click to place.

Move in XY, rotate around Z, and use positive X/Y scales with Z scale = 1. Select floors or anchors and **Make selected pieces fixed supports**.

To join touching edges, select both pieces. In **Connect**, click **Use two selected pieces**, **Propose shared-boundary bond**, then **Confirm connection**. Gaps and touching corners cannot bond. Save reusable groups as Unity prefabs.

## Test your level

1. In **Playtest**, if the level has no presets, choose **Medium** through **Presets**, then **Add selected preset to level**.
2. Click **Validate full level**, then **Enter Play mode**. Choose a captured explosion preset and Shift-click exterior air in Scene view.
3. Use **Reset to last captured start** to retry. Stop Play mode before editing; restart to capture changes. Saved pieces are preserved.

Blast origins inside solids or enclosed holes are unsupported. Player controls, multiplayer and bomb/fuse gameplay are not included.

## Contribute

Create a branch and open a pull request. Include asset `.meta` files; move and rename assets through Unity to preserve references. Code is in `Assets/BOM/Runtime` and `Assets/BOM/Editor`. Run EditMode and PlayMode tests in **Window > General > Test Runner** before submitting code changes.
