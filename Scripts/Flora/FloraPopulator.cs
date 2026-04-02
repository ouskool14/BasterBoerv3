using System.Collections.Generic;
using Godot;
using LandManagementSim.Terrain;

namespace WorldStreaming.Flora
{
    /// <summary>
    /// Render-only helper for converting FloraEntry simulation data into MultiMesh visual instances.
    /// Handles terrain alignment, scaling, MultiMesh population, and LOD.
    /// 
    /// REPLACES the old FloraPopulator completely. Key changes:
    /// - No placeholder mesh generation (use GLB paths or shared debug meshes)
    /// - Uses byte ArchetypeId instead of FloraType enum
    /// - Reads ChunkEcologyState for all visual variation
    /// - Expands FloraPatch into instances at chunk load time
    /// - Packs 4-float custom data for shader
    /// </summary>
    public static class FloraPopulator
    {
        // ── Mesh Paths ─────────────────────────────────────────────────────

        /// <summary>
        /// GLB mesh paths by archetype ID. Update when assets land.
        /// </summary>
        private static readonly string[] MeshPaths = {
            "res://Assets/Flora/flat_thorn.glb",        // 0 FlatThorn
            "res://Assets/Flora/upright_dryland.glb",   // 1 UprightDryland
            "res://Assets/Flora/round_landmark.glb",    // 2 RoundLandmark
            "res://Assets/Flora/dense_thorn_shrub.glb", // 3 DenseThornShrub
            "res://Assets/Flora/low_dry_bush.glb",      // 4 LowDryBush
            "res://Assets/Flora/dead_snag.glb",         // 5 DeadSnag
        };

        // ── Mesh Cache ─────────────────────────────────────────────────────

        /// <summary>
        /// Cache of loaded meshes. NOT static Dictionary - use instance-based caching
        /// to avoid stale references across scene reloads.
        /// </summary>
        private static Dictionary<byte, Mesh> _meshCache;

        /// <summary>
        /// Placeholder meshes for when GLBs don't exist yet.
        /// One shared placeholder per category (structural vs patch).
        /// </summary>
        private static Mesh _structuralPlaceholder;
        private static Mesh _patchPlaceholder;

        // ── Public API ─────────────────────────────────────────────────────

        /// <summary>
        /// Creates MultiMesh instances for all flora in a chunk.
        /// Called on background thread during chunk loading.
        /// 
        /// Returns Dictionary keyed by archetype ID (not FloraType).
        /// </summary>
        public static Dictionary<byte, MultiMesh> CreateFloraMultiMeshes(
            List<FloraEntry> structuralEntries,
            List<FloraPatch> patches,
            ChunkCoord chunkCoord,
            float chunkSize,
            ChunkEcologyState ecologyState)
        {
            var result = new Dictionary<byte, MultiMesh>();

            if ((structuralEntries == null || structuralEntries.Count == 0) &&
                (patches == null || patches.Count == 0))
            {
                return result;
            }

            Vector3 chunkOrigin = chunkCoord.GetWorldOrigin(chunkSize);

            // Group structural entries by archetype
            var structuralByArchetype = new Dictionary<byte, List<FloraEntry>>();
            if (structuralEntries != null)
            {
                foreach (var entry in structuralEntries)
                {
                    if (!entry.IsValid) continue;

                    if (!structuralByArchetype.ContainsKey(entry.ArchetypeId))
                    {
                        structuralByArchetype[entry.ArchetypeId] = new List<FloraEntry>();
                    }
                    structuralByArchetype[entry.ArchetypeId].Add(entry);
                }
            }

            // Expand patches and group by archetype
            var patchByArchetype = new Dictionary<byte, List<ExpandedPatchInstance>>();
            if (patches != null)
            {
                foreach (var patch in patches)
                {
                    var expanded = patch.Expand(ecologyState, chunkOrigin);
                    foreach (var instance in expanded)
                    {
                        if (!patchByArchetype.ContainsKey(instance.ArchetypeId))
                        {
                            patchByArchetype[instance.ArchetypeId] = new List<ExpandedPatchInstance>();
                        }
                        patchByArchetype[instance.ArchetypeId].Add(instance);
                    }
                }
            }

            // Create MultiMeshes for structural entries
            foreach (var kvp in structuralByArchetype)
            {
                byte archetypeId = kvp.Key;
                List<FloraEntry> entries = kvp.Value;

                MultiMesh multiMesh = CreateStructuralMultiMesh(entries, archetypeId, ecologyState, chunkOrigin);
                if (multiMesh != null)
                {
                    result[archetypeId] = multiMesh;
                }
            }

            // Create MultiMeshes for patch instances
            foreach (var kvp in patchByArchetype)
            {
                byte archetypeId = kvp.Key;
                List<ExpandedPatchInstance> instances = kvp.Value;

                MultiMesh multiMesh = CreatePatchMultiMesh(instances, archetypeId, ecologyState, chunkOrigin);
                if (multiMesh != null)
                {
                    // Use archetype ID + 128 to distinguish from structural
                    result[(byte)(archetypeId + 128)] = multiMesh;
                }
            }

            return result;
        }

        /// <summary>
        /// Clears the mesh cache. Call when changing quality settings or reloading assets.
        /// </summary>
        public static void ClearMeshCache()
        {
            _meshCache?.Clear();
            _structuralPlaceholder = null;
            _patchPlaceholder = null;
            GD.Print("[FloraPopulator] Mesh cache cleared");
        }

        /// <summary>
        /// Gets visibility range for an archetype (for LOD).
        /// </summary>
        public static float GetVisibilityRange(byte archetypeId)
        {
            return archetypeId switch
            {
                0 or 1 or 2 or 5 => 300f, // Structural trees - long range
                3 or 4 => 150f,           // Patch shrubs - medium range
                _ => 200f
            };
        }

        // ── Private Methods ────────────────────────────────────────────────

        /// <summary>
        /// Creates a MultiMesh for structural entries.
        /// </summary>
        private static MultiMesh CreateStructuralMultiMesh(
            List<FloraEntry> entries,
            byte archetypeId,
            ChunkEcologyState ecologyState,
            Vector3 chunkOrigin)
        {
            Mesh mesh = GetOrLoadMesh(archetypeId);
            if (mesh == null) return null;

            MultiMesh multiMesh = new MultiMesh
            {
                TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
                UseColors = true,
                UseCustomData = true,
                Mesh = mesh,
                InstanceCount = entries.Count
            };

            for (int i = 0; i < entries.Count; i++)
            {
                FloraEntry entry = entries[i];

                // Build transform
                Transform3D transform = BuildTransform(entry, chunkOrigin);
                multiMesh.SetInstanceTransform(i, transform);

                // Pack custom data for shader
                Color customData = PackCustomData(entry, ecologyState);
                multiMesh.SetInstanceCustomData(i, customData);

                // Set color (white - actual coloring done in shader via custom data)
                multiMesh.SetInstanceColor(i, Colors.White);
            }

            return multiMesh;
        }

        /// <summary>
        /// Creates a MultiMesh for patch instances.
        /// </summary>
        private static MultiMesh CreatePatchMultiMesh(
            List<ExpandedPatchInstance> instances,
            byte archetypeId,
            ChunkEcologyState ecologyState,
            Vector3 chunkOrigin)
        {
            Mesh mesh = GetOrLoadMesh(archetypeId);
            if (mesh == null) return null;

            MultiMesh multiMesh = new MultiMesh
            {
                TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
                UseColors = true,
                UseCustomData = true,
                Mesh = mesh,
                InstanceCount = instances.Count
            };

            for (int i = 0; i < instances.Count; i++)
            {
                ExpandedPatchInstance instance = instances[i];

                // Build transform
                Transform3D transform = BuildTransform(instance, chunkOrigin);
                multiMesh.SetInstanceTransform(i, transform);

                // Pack custom data for shader
                Color customData = PackCustomData(instance, ecologyState);
                multiMesh.SetInstanceCustomData(i, customData);

                // Set color
                multiMesh.SetInstanceColor(i, Colors.White);
            }

            return multiMesh;
        }

        /// <summary>
        /// Builds a Transform3D for a structural entry.
        /// </summary>
        private static Transform3D BuildTransform(FloraEntry entry, Vector3 chunkOrigin)
        {
            // Get terrain height at this position
            float terrainHeight = TerrainQuery.GetHeight(entry.WorldPosition2D.X, entry.WorldPosition2D.Y);

            // Local position relative to chunk origin
            Vector3 localPosition = new Vector3(
                entry.WorldPosition2D.X - chunkOrigin.X,
                terrainHeight,
                entry.WorldPosition2D.Y - chunkOrigin.Z
            );

            // Get terrain normal for alignment
            Vector3 terrainNormal = TerrainQuery.GetNormal(entry.WorldPosition2D.X, entry.WorldPosition2D.Y);

            // Build transform
            Transform3D transform = Transform3D.Identity;

            // Scale
            float baseScale = GetBaseScaleForArchetype(entry.ArchetypeId);
            float scale = entry.GetScaleMultiplier() * baseScale;
            transform = transform.Scaled(Vector3.One * scale);

            // Rotation: align to terrain + random Y rotation
            Basis alignedBasis = AlignToTerrainNormal(terrainNormal);
            alignedBasis = alignedBasis.Rotated(Vector3.Up, Mathf.DegToRad(entry.RotationY));
            transform.Basis = alignedBasis * transform.Basis;

            // Position
            transform.Origin = localPosition;

            return transform;
        }

        /// <summary>
        /// Builds a Transform3D for a patch instance.
        /// </summary>
        private static Transform3D BuildTransform(ExpandedPatchInstance instance, Vector3 chunkOrigin)
        {
            // Get terrain height
            float terrainHeight = TerrainQuery.GetHeight(instance.WorldPosition2D.X, instance.WorldPosition2D.Y);

            // Local position
            Vector3 localPosition = new Vector3(
                instance.WorldPosition2D.X - chunkOrigin.X,
                terrainHeight,
                instance.WorldPosition2D.Y - chunkOrigin.Z
            );

            // Get terrain normal
            Vector3 terrainNormal = TerrainQuery.GetNormal(instance.WorldPosition2D.X, instance.WorldPosition2D.Y);

            // Build transform
            Transform3D transform = Transform3D.Identity;

            // Scale
            float baseScale = GetBaseScaleForArchetype(instance.ArchetypeId);
            float scale = instance.ScaleMultiplier * baseScale;
            transform = transform.Scaled(Vector3.One * scale);

            // Rotation
            Basis alignedBasis = AlignToTerrainNormal(terrainNormal);
            alignedBasis = alignedBasis.Rotated(Vector3.Up, Mathf.DegToRad(instance.RotationY));
            transform.Basis = alignedBasis * transform.Basis;

            // Position
            transform.Origin = localPosition;

            return transform;
        }

        /// <summary>
        /// Packs custom data for shader into a Color (4 floats).
        /// 
        /// Channel 0: HueOffset (-0.08 to +0.08)
        /// Channel 1: Dryness (0-1)
        /// Channel 2: BurnTint (0-1)
        /// Channel 3: CanopyFill (0.3-1)
        /// </summary>
        private static Color PackCustomData(FloraEntry entry, ChunkEcologyState ecology)
        {
            float hueOffset = (entry.VariationSeed % 100f / 100f - 0.5f) * 0.16f;
            float dryness = ecology.GetDryness();
            float burnTint = ecology.GetBurnTint();
            float canopyFill = ecology.GetCanopyFill();

            return new Color(hueOffset, dryness, burnTint, canopyFill);
        }

        /// <summary>
        /// Packs custom data for a patch instance.
        /// </summary>
        private static Color PackCustomData(ExpandedPatchInstance instance, ChunkEcologyState ecology)
        {
            float hueOffset = (instance.VariationSeed % 100f / 100f - 0.5f) * 0.16f;
            float dryness = ecology.GetDryness();
            float burnTint = ecology.GetBurnTint();
            float canopyFill = ecology.GetCanopyFill() * 0.9f; // Patches slightly less full

            return new Color(hueOffset, dryness, burnTint, canopyFill);
        }

        /// <summary>
        /// Gets or loads the mesh for an archetype.
        /// </summary>
        private static Mesh GetOrLoadMesh(byte archetypeId)
        {
            // Initialize cache if needed
            _meshCache ??= new Dictionary<byte, Mesh>();

            // Check cache
            if (_meshCache.TryGetValue(archetypeId, out Mesh cachedMesh))
            {
                return cachedMesh;
            }

            // Try to load GLB
            if (archetypeId < MeshPaths.Length)
            {
                string path = MeshPaths[archetypeId];
                if (ResourceLoader.Exists(path))
                {
                    var mesh = GD.Load<Mesh>(path);
                    if (mesh != null)
                    {
                        _meshCache[archetypeId] = mesh;
                        return mesh;
                    }
                }
            }

            // Fall back to placeholder
            Mesh placeholder = GetPlaceholderMesh(archetypeId);
            _meshCache[archetypeId] = placeholder;
            return placeholder;
        }

        /// <summary>
        /// Gets a placeholder mesh for development before GLBs are ready.
        /// One shared placeholder per category (structural vs patch).
        /// </summary>
        private static Mesh GetPlaceholderMesh(byte archetypeId)
        {
            bool isStructural = FloraArchetypeIds.IsStructural(archetypeId);

            if (isStructural)
            {
                if (_structuralPlaceholder == null)
                {
                    _structuralPlaceholder = CreateSimpleTreeMesh();
                }
                return _structuralPlaceholder;
            }
            else
            {
                if (_patchPlaceholder == null)
                {
                    _patchPlaceholder = CreateSimpleBushMesh();
                }
                return _patchPlaceholder;
            }
        }

        /// <summary>
        /// Creates a simple placeholder tree mesh.
        /// </summary>
        private static Mesh CreateSimpleTreeMesh()
        {
            var surfaceTool = new SurfaceTool();
            surfaceTool.Begin(Mesh.PrimitiveType.Triangles);

            // Trunk: thin cylinder (6-sided prism)
            float trunkRadius = 0.1f;
            float trunkHeight = 1.5f;
            int trunkSides = 6;

            for (int i = 0; i < trunkSides; i++)
            {
                float angle1 = (float)i / trunkSides * Mathf.Pi * 2f;
                float angle2 = (float)(i + 1) / trunkSides * Mathf.Pi * 2f;

                float x1 = Mathf.Cos(angle1) * trunkRadius;
                float z1 = Mathf.Sin(angle1) * trunkRadius;
                float x2 = Mathf.Cos(angle2) * trunkRadius;
                float z2 = Mathf.Sin(angle2) * trunkRadius;

                // Trunk side face
                surfaceTool.AddVertex(new Vector3(x1, 0, z1));
                surfaceTool.AddVertex(new Vector3(x2, 0, z2));
                surfaceTool.AddVertex(new Vector3(x2, trunkHeight, z2));
                surfaceTool.AddVertex(new Vector3(x1, 0, z1));
                surfaceTool.AddVertex(new Vector3(x2, trunkHeight, z2));
                surfaceTool.AddVertex(new Vector3(x1, trunkHeight, z1));
            }

            // Canopy: flat-top canopy (box)
            float canopyWidth = 1.2f;
            float canopyHeight = 0.4f;
            float canopyY = trunkHeight;

            float hw = canopyWidth / 2f;

            // Top face
            surfaceTool.AddVertex(new Vector3(-hw, canopyY + canopyHeight, hw));
            surfaceTool.AddVertex(new Vector3(hw, canopyY + canopyHeight, hw));
            surfaceTool.AddVertex(new Vector3(hw, canopyY + canopyHeight, -hw));
            surfaceTool.AddVertex(new Vector3(-hw, canopyY + canopyHeight, hw));
            surfaceTool.AddVertex(new Vector3(hw, canopyY + canopyHeight, -hw));
            surfaceTool.AddVertex(new Vector3(-hw, canopyY + canopyHeight, -hw));

            // Side faces
            surfaceTool.AddVertex(new Vector3(-hw, canopyY, hw));
            surfaceTool.AddVertex(new Vector3(-hw, canopyY + canopyHeight, hw));
            surfaceTool.AddVertex(new Vector3(hw, canopyY + canopyHeight, hw));
            surfaceTool.AddVertex(new Vector3(-hw, canopyY, hw));
            surfaceTool.AddVertex(new Vector3(hw, canopyY + canopyHeight, hw));
            surfaceTool.AddVertex(new Vector3(hw, canopyY, hw));

            surfaceTool.GenerateNormals();
            return surfaceTool.Commit();
        }

        /// <summary>
        /// Creates a simple placeholder bush mesh.
        /// </summary>
        private static Mesh CreateSimpleBushMesh()
        {
            var surfaceTool = new SurfaceTool();
            surfaceTool.Begin(Mesh.PrimitiveType.Triangles);

            // Simple rounded box
            float width = 0.8f;
            float height = 0.6f;

            float hw = width / 2f;
            float hh = height;

            // Front face
            surfaceTool.AddVertex(new Vector3(-hw, 0, hw));
            surfaceTool.AddVertex(new Vector3(hw, 0, hw));
            surfaceTool.AddVertex(new Vector3(hw, hh, hw));
            surfaceTool.AddVertex(new Vector3(-hw, 0, hw));
            surfaceTool.AddVertex(new Vector3(hw, hh, hw));
            surfaceTool.AddVertex(new Vector3(-hw, hh, hw));

            // Back face
            surfaceTool.AddVertex(new Vector3(hw, 0, -hw));
            surfaceTool.AddVertex(new Vector3(-hw, 0, -hw));
            surfaceTool.AddVertex(new Vector3(-hw, hh, -hw));
            surfaceTool.AddVertex(new Vector3(hw, 0, -hw));
            surfaceTool.AddVertex(new Vector3(-hw, hh, -hw));
            surfaceTool.AddVertex(new Vector3(hw, hh, -hw));

            // Top face
            surfaceTool.AddVertex(new Vector3(-hw, hh, hw));
            surfaceTool.AddVertex(new Vector3(hw, hh, hw));
            surfaceTool.AddVertex(new Vector3(hw, hh, -hw));
            surfaceTool.AddVertex(new Vector3(-hw, hh, hw));
            surfaceTool.AddVertex(new Vector3(hw, hh, -hw));
            surfaceTool.AddVertex(new Vector3(-hw, hh, -hw));

            surfaceTool.GenerateNormals();
            return surfaceTool.Commit();
        }

        /// <summary>
        /// Gets base scale for an archetype.
        /// </summary>
        private static float GetBaseScaleForArchetype(byte archetypeId)
        {
            return archetypeId switch
            {
                0 => 1.2f,  // FlatThorn
                1 => 1.0f,  // UprightDryland
                2 => 1.1f,  // RoundLandmark
                3 => 0.7f,  // DenseThornShrub
                4 => 0.5f,  // LowDryBush
                5 => 0.9f,  // DeadSnag
                _ => 1.0f
            };
        }

        /// <summary>
        /// Aligns a basis to terrain normal while maintaining upward growth.
        /// </summary>
        private static Basis AlignToTerrainNormal(Vector3 terrainNormal)
        {
            Vector3 up = terrainNormal.Normalized();
            Vector3 right = Vector3.Up.Cross(up);

            // Handle case where terrain is perfectly flat
            if (right.LengthSquared() < 0.001f)
            {
                right = Vector3.Right;
            }
            else
            {
                right = right.Normalized();
            }

            Vector3 forward = up.Cross(right).Normalized();
            return new Basis(right, up, forward);
        }
    }
}
