## Implementation Plan
Two independent fixes are required for the fence system not rendering:

### Fix 1: Test Fence Visibility by Adjusting MapInsetPercent
- **File**: FenceSystem.cs (Inspector property, no code change needed)
- **Change**: In the Godot Editor, select the FenceSystem node and set `MapInsetPercent = 0.05` (instead of 0.95) to move the fence perimeter to ±100m from origin (instead of ±1900m)
- **Verification**: Fence should now be visible near the player spawn point
- **After Testing**: Restore `MapInestPercent = 0.95` for production

### Fix 2: Assign Required Mesh Scenes in Inspector
- **File**: FenceSystem.cs (Inspector properties)
- **Change**: In the Godot Editor, select the FenceSystem node and assign:
  - `PoleMeshScene`: Drag a .glb file containing fence pole mesh
  - `StickMeshScene`: Drag a .glb file containing fence dropper/stick mesh  
  - `WireMeshScene`: Drag a .glb file containing barbed wire segment mesh
- **If Assets Unavailable**: Create temporary placeholder meshes:
  1. Create a new MeshInstance3D node
  2. Set its mesh to a primitive (BoxMesh, CylinderMesh, etc.)
  3. Save the node as a .tscn file
  4. Assign the .tscn to the appropriate mesh scene slot
- **Verification**: Check Output log for `[FenceSystem] Meshes ready: Pole=True, Stick=True, Wire=True`

## Code Changes
No code changes are required in FenceSystem.cs as both issues are resolved through Inspector configuration. The system already contains appropriate diagnostic logging:

- Line 98-100: Prints mesh scene slot status on startup
- Line 112: Prints mesh extraction results  
- Lines 118-122: Warns if all meshes are null
- Lines 433-435: Reports chunks built with visible geometry
- Lines 437-442: Warns if no chunk has geometry
- Lines 456-459: Prints fence perimeter location

## Expected Output After Fixes
When both issues are resolved, the Output log should show:
```
[FenceSystem] Scene slots: Pole=res://.../pole.glb, Stick=res://.../stick.glb, Wire=res://.../wire.glb
[FenceSystem] Meshes ready: Pole=True, Stick=True, Wire=True
[FenceSystem] Generation done: X chunks, Y poles, Z sticks, W wire segments.
[FenceSystem] BuildVisuals() done. X chunk nodes created, Y have visible geometry.
[FenceSystem] *** FENCE IS AT perimeter ±(100.0, 100.0) world units from origin. ***
```

## Important Notes
1. The fence generation runs on a background thread - expect a short delay between startup and visual appearance
2. `DebugForceVisible = true` can be set to bypass chunk streaming for immediate testing
3. Always restore `MapInsetPercent = 0.95` after testing to maintain correct fence placement at map boundaries