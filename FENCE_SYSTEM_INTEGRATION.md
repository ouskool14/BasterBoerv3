## 1. Scene Setup

Attach `FenceSystem.cs` to a Node3D in your main world scene.

Assign the following in the Inspector:

- PoleMesh → mesh of fence pole
- StickMesh → mesh of smaller vertical sticks
- WireMesh → barbed wire mesh

IMPORTANT:
Meshes must be **Mesh resources**, NOT PackedScenes.

---

## 2. GameState Requirements

Ensure a singleton exists at:


/root/GameState


With fields:


float MapSizeX
float MapSizeZ
int WorldSeed


---

## 3. Terrain Height Integration (CRITICAL)

Replace:

```csharp
return 0f;

With ONE of the following:

Option A — Noise
return noise.GetNoise2D(x, z) * heightScale;
Option B — Heightmap (Preferred)
return heightMap[xIndex, zIndex];

Heightmap lookup is strongly preferred for performance.

4. Chunk Size Alignment

Ensure this matches your world streamer:

ChunkSize = your chunk size (e.g. 100f)
5. Performance Guarantees

This system:

Uses NO physics queries
Uses NO GetNode in loops
Uses MultiMesh (GPU instancing)
Uses background threading
Minimizes allocations
6. Next Step (Required for Large Worlds)

Integrate with WorldChunkStreamer:

Each chunk node:

Must be registered
Must be hidden/unloaded when far from player

Suggested:

chunkNode.Visible = false;

Controlled externally by streamer.

7. Expected Scale

This system is designed to handle:

Tens of thousands of fence elements
Large open worlds
Coexistence with large animal populations
8. Do NOT
Do NOT reintroduce raycasting
Do NOT instantiate scenes per fence post
Do NOT use individual MeshInstance3D per object
9. Optional Future Improvements
GPU-based terrain sampling
Job system per chunk
LOD system for distant fences
Object pooling for MultiMesh reuse
END

---

If needed, the next logical step is integrating this cleanly into your **WorldChunkStreamer**, which will determine whether your game scales to thousands of animals smoothly.