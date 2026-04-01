## Task
The animals in the game are not being rendered on screen. This appears to be related to recent changes made to implement terrain tilt (Gate 5) in the animal rendering system, which may have introduced an error in the basis calculation or mesh setup that prevents the MultiMesh instances from displaying correctly.

## Files to Read
- AnimalRenderer.cs — Contains the rendering logic for animals, including interpolation and terrain tilt. The issue likely lies in the _Process method where MultiMesh transforms are updated, or in the GetTerrainAlignedBasis method.
- AnimalSystem.cs — Manages the simulation of herds and animals. We need to verify that herds and animals are being created and updated correctly.
- GameState.cs — Provides the player node reference used for distance culling. If the player node is not found, it could cause incorrect culling.
- TerrainQuery.cs — Not provided, but referenced. Responsible for heightmap queries used in rendering.
- AnimalInterpolation_ImplementationPlan.md — Describes the intended implementation of interpolation and terrain tilt, which can help us understand what was supposed to be done.

## Architecture Rules for This Task
- Any object appearing more than once in the world uses `MultiMeshInstance3D`. Unique meshes per instance will blow the draw call budget. (We are using MultiMesh, so this is followed.)
- Visual variety (size, coat colour, health state) is achieved via per-instance shader parameters, not separate meshes or materials. (We are using MultiMesh, so this is followed.)
- `GetNode()` must never be called inside loops or `_Process()`. Cache references in `_Ready()`. (We see _player is cached in _Ready, so this is followed.)
- No LINQ in hot paths. Use direct array iteration. (We see for loops, so this is followed.)

## Constraints and Gotchas
- The terrain tilt implementation (Gate 5) must be done once per herd, not once per animal, to avoid excessive heightmap queries. The current implementation in AnimalRenderer.cs calls GetTerrainAlignedBasis per animal, which is inefficient and may cause performance issues or errors if the heightmap query fails for some animals.
- If the player node named "Boer" is not found in the scene, _player will be null, causing the player position to default to (0,0,0). This could lead to incorrect distance culling if the animals are far from the origin.
- The MultiMeshInstance3D nodes will not render if the associated mesh is null, which happens if the GLB scenes fails to load or does not contain a MeshInstance3D.

## Output Format
Provide an implementation plan first — a short description of every change required and which file it goes in.
Then provide the code for each change.
Specify: file name, method name or region, and exact insertion point for each block.
Do not rewrite entire files. Show only what changes.
If a decision requires design input from the project owner, flag it explicitly rather than making an assumption.