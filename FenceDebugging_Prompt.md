## Task
The fence system is not rendering in the game. This has two independent root causes that must both be resolved:
1. The fence is generated at the map edge (approximately 1900m from origin) due to MapInsetPercent = 0.95, making it outside the player's view range
2. The required mesh scene slots (PoleMeshScene, StickMeshScene, WireMeshScene) are likely unassigned in the Inspector, causing geometry to not be visible even if fence data is generated

## Files to Read
- FenceSystem.cs — Contains the fence generation logic, Inspector-exposed parameters, and diagnostic logging that identifies both problems
- No other files need to be modified as this is purely an Inspector/setup issue

## Architecture Rules for This Task
- Keep GDScript thin — delegate to C# systems. (FenceSystem is a Node3D with C# logic, which is acceptable)
- Use typed GDScript where possible. (Not applicable as this is C# with Godot attributes)
- Signal-based communication between nodes. (Not applicable to this issue)

## Constraints and Gotchas
- The fence generation runs on a background thread and only builds visuals on the main thread via CallDeferred
- If all three mesh slots are null, the system will generate fence data but create no visible geometry, printing a warning to the Output log
- The MapInsetPercent value directly controls where the fence perimeter is placed relative to map size
- DebugForceVisible can be used to bypass chunk streaming for testing

## Output Format
Provide an implementation plan first — a short description of every change required and which file it goes in.
Then provide the code for each change.
Specify: file name, method name or region, and exact insertion point for each block.
Do not rewrite entire files. Show only what changes.
If a decision requires design input from the project owner, flag it explicitly rather than making an assumption.