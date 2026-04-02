using Godot;

namespace WorldStreaming.Terrain
{
    /// <summary>
    /// ScriptableObject containing all tweakable terrain generation parameters.
    /// Allows designers to tune the world without modifying code.
    /// </summary>
    [GlobalClass]
    public partial class TerrainConfig : Resource
    {
        #region World Generation Settings
        
        [ExportGroup("World Generation")]
        [Export]
        public int WorldSeed { get; set; } = 12345;
        
        [Export]
        public float ChunkSize { get; set; } = 256f;
        
        [Export]
        public int ChunkResolution { get; set; } = 128;
        
        [Export]
        public float HeightScale { get; set; } = 40f;
        
        #endregion
        
        #region Macro Landform Settings
        
        [ExportGroup("Macro Landform")]
        [Export(PropertyHint.Range, "0.0001, 0.01")]
        public float MacroNoiseFrequency { get; set; } = 0.0008f;
        
        [Export(PropertyHint.Range, "1, 8")]
        public int MacroOctaves { get; set; } = 4;
        
        [Export(PropertyHint.Range, "0.1, 1.0")]
        public float MacroPersistence { get; set; } = 0.5f;
        
        [Export(PropertyHint.Range, "1.5, 3.0")]
        public float MacroLacunarity { get; set; } = 2.0f;
        
        [Export(PropertyHint.Range, "0.0, 1.0")]
        public float RidgeInfluence { get; set; } = 0.4f;
        
        [Export(PropertyHint.Range, "0.0, 1.0")]
        public float ValleyInfluence { get; set; } = 0.3f;
        
        #endregion
        
        #region Flat Area Settings
        
        [ExportGroup("Flat Areas")]
        [Export(PropertyHint.Range, "0.0005, 0.005")]
        public float FlatAreaFrequency { get; set; } = 0.0015f;
        
        [Export(PropertyHint.Range, "0.0, 1.0")]
        public float FlatAreaThreshold { get; set; } = 0.6f;
        
        [Export(PropertyHint.Range, "50, 500")]
        public float MinFlatAreaSize { get; set; } = 150f;
        
        [Export(PropertyHint.Range, "100, 1000")]
        public float MaxFlatAreaSize { get; set; } = 400f;
        
        [Export(PropertyHint.Range, "0.0, 1.0")]
        public float FlatAreaBlendStrength { get; set; } = 0.7f;
        
        #endregion
        
        #region Ridge and Hill Settings
        
        [ExportGroup("Ridges & Hills")]
        [Export(PropertyHint.Range, "0.0, 1.0")]
        public float RidgeIntensity { get; set; } = 0.5f;
        
        [Export(PropertyHint.Range, "0.001, 0.01")]
        public float RidgeFrequency { get; set; } = 0.003f;
        
        [Export(PropertyHint.Range, "0.0, 1.0")]
        public float HillDensity { get; set; } = 0.4f;
        
        [Export(PropertyHint.Range, "0.001, 0.008")]
        public float HillFrequency { get; set; } = 0.0025f;
        
        [Export(PropertyHint.Range, "0.0, 1.0")]
        public float DirectionalBias { get; set; } = 0.3f;
        
        #endregion
        
        #region Hydrology Settings
        
        [ExportGroup("Hydrology")]
        [Export(PropertyHint.Range, "0.0, 1.0")]
        public float DrainageDensity { get; set; } = 0.5f;
        
        [Export(PropertyHint.Range, "0.001, 0.01")]
        public float FlowNoiseFrequency { get; set; } = 0.002f;
        
        [Export(PropertyHint.Range, "0.0, 1.0")]
        public float RiverCarvingDepth { get; set; } = 0.3f;
        
        [Export(PropertyHint.Range, "0.0, 1.0")]
        public float MoistureSpread { get; set; } = 0.4f;
        
        #endregion
        
        #region Waterhole Settings
        
        [ExportGroup("Waterholes")]
        [Export(PropertyHint.Range, "0, 50")]
        public int WaterholeCount { get; set; } = 15;
        
        [Export(PropertyHint.Range, "30, 100")]
        public float WaterholeMinRadius { get; set; } = 40f;
        
        [Export(PropertyHint.Range, "50, 200")]
        public float WaterholeMaxRadius { get; set; } = 100f;
        
        [Export(PropertyHint.Range, "2, 10")]
        public float WaterholeDepth { get; set; } = 5f;
        
        [Export(PropertyHint.Range, "0.0, 1.0")]
        public float WaterholeBasinSteepness { get; set; } = 0.5f;
        
        #endregion
        
        #region Erosion Settings
        
        [ExportGroup("Erosion")]
        [Export(PropertyHint.Range, "0.0, 1.0")]
        public float ErosionStrength { get; set; } = 0.4f;
        
        [Export(PropertyHint.Range, "0.0, 1.0")]
        public float GullyFormation { get; set; } = 0.3f;
        
        [Export(PropertyHint.Range, "0.0, 1.0")]
        public float RockExposure { get; set; } = 0.35f;
        
        [Export(PropertyHint.Range, "0.001, 0.01")]
        public float ErosionNoiseFrequency { get; set; } = 0.004f;
        
        #endregion
        
        #region Road Settings
        
        [ExportGroup("Roads")]
        [Export(PropertyHint.Range, "0.0, 1.0")]
        public float RoadDensity { get; set; } = 0.25f;
        
        [Export(PropertyHint.Range, "5, 20")]
        public float RoadWidth { get; set; } = 8f;
        
        [Export(PropertyHint.Range, "0.0, 1.0")]
        public float RoadFlattening { get; set; } = 0.6f;
        
        [Export(PropertyHint.Range, "0.0, 0.5")]
        public float RoadDepressionDepth { get; set; } = 0.15f;
        
        #endregion
        
        #region Biome/Soil Settings
        
        [ExportGroup("Biome & Soil")]
        [Export(PropertyHint.Range, "0.0, 1.0")]
        public float Rockiness { get; set; } = 0.3f;
        
        [Export(PropertyHint.Range, "0.0, 1.0")]
        public float SoilVariety { get; set; } = 0.5f;
        
        [Export(PropertyHint.Range, "0.001, 0.01")]
        public float BiomeNoiseFrequency { get; set; } = 0.002f;
        
        #endregion
        
        #region Seasonal Settings
        
        [ExportGroup("Seasonal Response")]
        [Export(PropertyHint.Range, "0.0, 1.0")]
        public float WetnessResponse { get; set; } = 0.6f;
        
        [Export(PropertyHint.Range, "0.0, 1.0")]
        public float DrySeasonBleaching { get; set; } = 0.4f;
        
        [Export(PropertyHint.Range, "0.0, 1.0")]
        public float GreenFlushIntensity { get; set; } = 0.7f;
        
        [Export(PropertyHint.Range, "0.0, 1.0")]
        public float DrainageActivation { get; set; } = 0.5f;
        
        #endregion
        
        #region Performance Settings
        
        [ExportGroup("Performance")]
        [Export(PropertyHint.Range, "1, 4")]
        public int MaxConcurrentChunkBuilds { get; set; } = 2;
        
        [Export(PropertyHint.Range, "16, 256")]
        public int HeightmapCacheSize { get; set; } = 64;
        
        [Export]
        public bool EnableDebugVisualization { get; set; } = false;
        
        #endregion
        
        /// <summary>
        /// Creates a default configuration for South African bushveld terrain.
        /// </summary>
        public static TerrainConfig CreateDefaultBushveldConfig()
        {
            return new TerrainConfig
            {
                WorldSeed = 12345,
                HeightScale = 35f,
                RidgeIntensity = 0.45f,
                FlatAreaThreshold = 0.55f,
                WaterholeCount = 12,
                DrainageDensity = 0.4f,
                ErosionStrength = 0.35f,
                RoadDensity = 0.2f,
                Rockiness = 0.3f
            };
        }
        
        /// <summary>
        /// Creates a configuration with dramatic terrain for testing.
        /// </summary>
        public static TerrainConfig CreateDramaticConfig()
        {
            return new TerrainConfig
            {
                WorldSeed = 99999,
                HeightScale = 60f,
                RidgeIntensity = 0.8f,
                ValleyInfluence = 0.6f,
                ErosionStrength = 0.7f,
                Rockiness = 0.6f,
                WaterholeCount = 8
            };
        }
    }
}
