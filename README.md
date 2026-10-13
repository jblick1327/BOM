# BOM

Build destructible structures in Unity: import a model, edit its outline, place and connect pieces, then try explosions.

## Open the project

1. Install **Unity 6000.6.5f1** through Unity Hub.
2. Clone this repository with GitHub Desktop, or download and unzip it.
3. In Unity Hub, choose **Add project from disk** and select the folder containing `Assets`, `Packages` and `ProjectSettings`. Open it and wait for importing to finish.
4. Open `Assets/Scenes/StarterLevel.unity`. Select **BOM Level** in the Hierarchy, then choose **BOM > Authoring workspace**.

The starter has an empty level group and a subtly angled camera. Nothing is placed yet. No command-line tools are needed.

## Optional: try the example

Open `Assets/BOM/Fixtures/ReferenceLevel.unity` and select **BOM Reference Level** in the Hierarchy.

In **Playtest**, click **Validate full level**, then **Enter Play mode**. Choose a captured explosion preset and **Shift-click outside the structure in Scene view** to blast it.

Use **Reset to last captured start** to try again. Destruction does not change the saved pieces. Stop Play mode before editing.

## Make an asset

1. Export a static FBX from Blender and drag it into Unity's Project window. In **Import / review**, drag that imported model into **Model (FBX)**.
2. Choose your projection plane: **XY** for the front, **XZ** for the top, or **YZ** for the side. Coordinates are in metres.
3. Drag outline points. Double-click an edge to add a point; select a point and press **Delete** to remove it. **Add hole** creates an opening. Scroll to zoom, middle-drag to pan, or use **Fit outline**. Escape cancels a drag; Ctrl+Z undoes edits.
4. Choose or create a **Gameplay material** (density and blast resistance) and a **Destruction response** (fragment impulse). Review thickness, colours and the optional face image in the 3D preview.
5. Resolve validation messages, then click **Approve and continue to placement** and save the piece. Exact coordinates and texture mapping are under **Advanced**.

Keep your models and pieces in your own folders under `Assets`. Use **Review existing piece** to edit a saved piece; changes apply only when approved.

## Build a level

Save a copy of StarterLevel in your own level folder. In **Place**, select its level group, choose a **Piece**, then click **Place piece**. Use **Create level authoring group** for a separate group.

Move pieces with Scene handles: keep Z = 0, rotate around Z, and use positive X/Y scales. Select floors or anchors and click **Make selected pieces fixed supports**. Placement Inspectors offer overrides without changing the shared piece asset.

To join pieces, make their edges touch and select both. In **Connect**, click **Use two selected pieces**, **Propose shared-boundary bond**, then **Confirm connection**. Gaps and touching corners are not bonds. Save reusable groups as ordinary Unity prefabs.

## Test your level

In **Playtest**, choose or **Create preset and add to level...**. Set its radius, reach budget and fragment impulse speed in the Inspector. Validate and enter Play mode. Restart the test to include changed presets or authoring inputs.

Explosions must start in exterior air, not inside solid pieces or enclosed holes. Player controls, multiplayer and bomb/fuse gameplay are not included.

## Contribute

Create a branch and open a pull request with your assets or scenes. Include their `.meta` files. Move and rename assets through Unity's Project window to preserve references.

<details>
<summary>For programmers</summary>

Code lives in `Assets/BOM/Runtime` and `Assets/BOM/Editor`; tests are in `Assets/BOM/Tests`. Before submitting code changes, run EditMode and PlayMode tests in **Window > General > Test Runner**.

</details>
