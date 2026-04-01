## Task
Animals are not being rendered in the game world. This appears to be related to recent changes implementing animal interpolation and terrain tilt (Gate 5), which may have introduced errors in the rendering pipeline that prevent MultiMesh instances from displaying correctly. The issue needs to be diagnosed and fixed while maintaining the simulation/render separation.

## Files to Read
- AnimalRenderer.cs — Primary rendering system for animals. Contains the interpolation logic, terrain tilt implementation, and MultiMesh update code where the issue likely resides.
- AnimalSystem.cs — Manages herd and animal simulation. Need to verify that herds are being created and updated correctly.
- GameState.cs — Provides player reference for distance culling. If player node is not found, it could cause incorrect culling.
- AnimalInterpolation_ImplementationPlan.md — Describes the intended interpolation implementation, which helps understand what was supposed to be implemented vs what was actually done.

## Architecture Rules for This Task
- Simulation systems never instantiate Godot scene nodes. That is the render layer's job exclusively. (AnimalRenderer should only read from AnimalSystem, not modify it)
- Nothing heavy runs in `_Process()`. Simulation logic subscribes to `TimeSystem` tick signals. (Calling AnimalSystem.UpdateFrame every frame violates this)
- `GetNode()` must never be called inside loops or `_Process()`. Cache references in `_Ready()`. (Player reference is cached in _Ready, which is correct)
- Any object appearing more than once in the world uses `MultiMeshInstance3D`. Unique meshes per instance will blow the draw call budget. (Correctly using MultiMesh)
- No LINQ in hot paths. Use direct array iteration. (Visible in for loops, which is correct)

## Constraints and Gotchas
- The terrain tilt implementation (Gate 5) must be done once per herd, not once per animal, to avoid excessive heightmap queries. Current implementation may be calling GetTerrainAlignedBasis per animal.
- If the player node named "Boer" is not found in the scene, _player will be null, causing playerPos to default to (0,0,0), which could lead to incorrect distance culling if animals are far from origin.
- The MultiMeshInstance3D nodes will not render if the associated mesh is null, which happens if GLB scenes fail to load or don't contain MeshInstance3D.
- Calling AnimalSystem.UpdateFrame every frame in _Process violates the simulate/render separation and could cause simulation instability.

## Output Format
Provide an implementation plan first — a short description of every change required and which file it goes in.
Then provide the code for each change.
Specify: file name, method name or region, and exact insertion point for each block.
Do not rewrite entire files. Show only what changes.
If a decision requires design input from the project owner, flag it explicitly rather than making an assumption.