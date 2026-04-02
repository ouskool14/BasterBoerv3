# BasterBoerv3 - Script Guide

> **Quick Reference for Understanding the Codebase**
> 
> This document provides plain-English explanations of what each script does, its key public APIs, and how scripts depend on each other.

---

## Table of Contents

1. [Architecture Overview](#architecture-overview)
2. [Animal System](#-animal-system-the-herd-simulation-core)
3. [Building System](#-building-system)
4. [Economy System](#-economy-system)
5. [Time & Seasons](#-time--seasons-system)
6. [Terrain & World Streaming](#-terrain--world-streaming)
7. [Water System](#-water-system)
8. [Grazing & Ecology](#-grazing--ecology-system)
9. [Flora System](#-flora-system-trees--vegetation)
10. [Staff System](#-staff-system)
11. [Weather & Environment](#-weather--environment)
12. [Fence System](#-fence-system)
13. [Core Game Systems](#-core-game-systems)
14. [Dependency Graph](#dependency-graph)

---

## Architecture Overview

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                           BasterBoerv3 Architecture                         │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                             │
│  ┌──────────────┐     ┌──────────────┐     ┌──────────────┐                │
│  │  TimeSystem  │────▶│ AnimalSystem │◀────│HerdFactory   │                │
│  │  (heartbeat) │     │ (herd mgr)   │     │(spawning)    │                │
│  └──────┬───────┘     └──────┬───────┘     └──────────────┘                │
│         │                    │                                              │
│         │         ┌──────────┴──────────┐                                   │
│         │         │                     │                                   │
│         ▼         ▼                     ▼                                   │
│  ┌──────────────┐  ┌──────────────┐  ┌──────────────┐                       │
│  │EconomySystem │  │  HerdBrain   │  │AnimalRenderer│                       │
│  │  (finances)  │  │   (AI)       │  │  (visuals)   │                       │
│  └──────────────┘  └──────┬───────┘  └──────────────┘                       │
│                           │                                                 │
│                    ┌──────┴──────┐                                          │
│                    │AnimalStruct │                                          │
│                    │  (data)     │                                          │
│                    └─────────────┘                                          │
│                                                                             │
│  ┌─────────────────────────────────────────────────────────────────────┐   │
│  │                    World Streaming Layer                            │   │
│  │  ┌──────────────┐  ┌──────────────┐  ┌──────────────┐              │   │
│  │  │WorldChunk    │──▶│  TerrainGen  │  │ FloraSystem  │              │   │
│  │  │Streamer      │  │              │  │              │              │   │
│  │  └──────────────┘  └──────────────┘  └──────────────┘              │   │
│  └─────────────────────────────────────────────────────────────────────┘   │
│                                                                             │
│  ┌─────────────────────────────────────────────────────────────────────┐   │
│  │                      Support Systems                                │   │
│  │  ┌──────────┐ ┌──────────┐ ┌──────────┐ ┌──────────┐ ┌──────────┐  │   │
│  │  │WaterSys  │ │GrazingSys│ │Building  │ │StaffSys  │ │FenceSys  │  │   │
│  │  │          │ │          │ │System    │ │          │ │          │  │   │
│  │  └──────────┘ └──────────┘ └──────────┘ └──────────┘ └──────────┘  │   │
│  └─────────────────────────────────────────────────────────────────────┘   │
│                                                                             │
│  ┌─────────────────────────────────────────────────────────────────────┐   │
│  │                      Central Data Store                             │   │
│  │                         GameState                                   │   │
│  └─────────────────────────────────────────────────────────────────────┘   │
│                                                                             │
└─────────────────────────────────────────────────────────────────────────────┘
```

---

## 🐄 Animal System (The Herd Simulation Core)

### AnimalSystem.cs
**Purpose:** Central manager for all animal herds in the game. Singleton pattern - only one instance exists.

**What it does:**
- Maintains a list of all `HerdBrain` objects (one per herd)
- Updates herds every frame with LOD-aware ticking (nearby = full AI, distant = simplified)
- Handles time-based events: daily ticks (health), monthly ticks (aging/death/reproduction)
- Provides query interface: "find herds within radius", "alert herds to danger"
- Manages animal ID generation and total animal counts

**Key Methods:**
```csharp
// Singleton access
public static AnimalSystem Instance { get; }

// Main update - called every frame from game loop
public void UpdateFrame(float deltaTime, Vector3 playerPosition)

// Time-based events
public void OnDailyTick()     // Health effects
public void OnMonthlyTick()   // Aging, death, reproduction

// Queries
public IReadOnlyList<HerdBrain> GetHerdsInRadius(Vector3 center, float radius)
public void AlertHerdsToThreat(Vector3 threatPosition, float radius, float fearLevel)

// Herd management
public void AddHerd(HerdBrain herd)
public void RemoveHerd(HerdBrain herd)
public ulong GetNextAnimalId()

// Statistics
public int TotalAnimalCount { get; }  // Sum across all herds
public IReadOnlyList<HerdBrain> Herds { get; }
```

**Dependencies:**
- Calls: `HerdBrain.Tick()`, `HerdBrain.OnDailyTick()`, `HerdBrain.OnMonthlyTick()`
- Called by: Game loop (main), `TimeSystem` (scheduled events)

---

### HerdBrain.cs
**Purpose:** AI controller for a single herd (e.g., a group of kudu). The "brain" that makes decisions for the entire herd.

**What it does:**
- Maintains shared herd state: thirst, hunger, fatigue, fear level
- Runs a state machine: Grazing → Moving → Drinking → Resting → Fleeing → Alerting
- Owns an array of `AnimalStruct` (individual animals)
- Applies biological variation so each animal moves slightly differently
- Handles reproduction timers, finds water sources
- Implements LOD-aware tick rates (update frequency based on player distance)

**Key Methods:**
```csharp
// Constructor
public HerdBrain(Species species, SpeciesConfig config, Vector3 center, AnimalStruct[] animals, int seed)

// Main update - called every frame for nearby herds
public void Tick(float deltaTime, Vector3 playerPosition)

// State machine
private void ExecuteCurrentState(float deltaTime)  // State machine logic
private void TransitionToState(HerdState newState)

// Behavior states
private void ExecuteGrazing(float deltaTime)
private void ExecuteMoving(float deltaTime)
private void ExecuteDrinking(float deltaTime)
private void ExecuteResting(float deltaTime)
private void ExecuteFleeing(float deltaTime)

// Time-based events
public void OnDailyTick()    // Health updates
public void OnMonthlyTick()  // Aging, death, reproduction

// Reproduction
private void TryReproduce(float deltaTime)
private void SpawnOffspring(int count)

// Water seeking
private bool TryFindWaterSource()
private Vector3 GetWaterSeekPosition()

// Queries
public Vector3 CenterPosition { get; }           // Herd center in world space
public AnimalStruct[] Animals { get; }           // All animals in herd
public HerdState CurrentState { get; }           // Current behavior state
public float FearLevel { get; }                  // 0-1 fear level

// LOD
public void SetLODLevel(BehaviourLOD lod)        // Adjust update frequency
```

**Dependencies:**
- Calls: `AnimalMovement.ValidateMove()`, `AnimalMovement.SnapToTerrain()`, `TerrainQuery.GetHeight()`, `WaterSystem.Instance.FindNearestWaterSource()`
- Called by: `AnimalSystem` (main update), `TimeSystem` (scheduled events)

---

### AnimalStruct.cs
**Purpose:** Lightweight data container for a single animal. Struct (not class) for memory efficiency.

**What it does:**
- Stores pure data - NO behavior logic (all decisions from `HerdBrain`)
- Position offset from herd center (contiguous memory = faster iteration)
- Contains: age, health, genetics, animation state, unique ID

**Key Fields:**
```csharp
public Vector3 WorldPosition;        // Offset from herd center
public float Age;                    // In months
public float Health;                 // 0.0 (dead) to 1.0 (perfect)
public GeneticProfile Genetics;      // Immutable traits
public int MeshInstanceId;           // Render layer reference (-1 if not rendered)
public AnimalSex Sex;                // Male/Female
public AnimationState CurrentAnimation;
public ulong UniqueId;               // For save/load and tracking
public float NextVariationTime;      // Timer for behavior variation
```

**Key Methods:**
```csharp
// Factory method
public static AnimalStruct Create(Vector3 position, AnimalSex sex, ulong id, Random rng)

// Helpers
public bool IsAdult => Age >= 12f;   // 12 months = adult
public bool IsAlive => Health > 0f;
```

**Dependencies:**
- Called by: `HerdFactory` (creation), `HerdBrain` (updates), `AnimalRenderer` (rendering)

---

### AnimalRenderer.cs
**Purpose:** Visual representation layer - bridges pure data simulation to the visual world.

**What it does:**
- Reads herd data from `AnimalSystem` every frame
- Renders using `MultiMeshInstance3D` (one per species for performance)
- Handles interpolation for smooth movement (two-tier system)
- Only renders animals within 800 meters
- Can spawn test herds for debugging

**Key Methods:**
```csharp
// Godot lifecycle
public override void _Ready()
public override void _Process(double delta)  // Main render loop

// Core rendering
private void UpdateVisibleHerds()            // Update MultiMesh transforms
private void UpdateHerdVisuals(HerdBrain herd, Species species)
private void UpdateIndividualAnimations(HerdBrain herd, float delta)

// Interpolation (smooth movement)
private HerdRenderState UpdateHerdInterpolation(HerdBrain herd, float delta)
private void UpdateAnimalOffsets(HerdBrain herd, HerdRenderState herdState, float delta)

// Test herd spawning
private void SpawnTestHerdIfRequested()

// Inspector properties (set in Godot editor)
[Export] public PackedScene KuduScene { get; set; }
[Export] public PackedScene ImpalaScene { get; set; }
[Export] public PackedScene ZebraScene { get; set; }
[Export] public float MaxRenderDistance { get; set; } = 800f;
[Export] public bool SpawnTestHerd { get; set; } = true;
```

**Dependencies:**
- Calls: `AnimalSystem.Instance.Herds`, `HerdBrain.CenterPosition`, `HerdBrain.Animals`
- Called by: Godot engine (auto)

---

### AnimalMovement.cs
**Purpose:** Static utility class for terrain-aware movement validation.

**What it does:**
- Validates moves (checks walkable terrain, slope steepness, drop depth)
- Tries alternative paths with perpendicular offsets when blocked
- Snaps positions to terrain height
- Uses `TerrainQuery` for efficient lookups (no raycasts)

**Key Methods:**
```csharp
// Validation
public static bool ValidateMove(Vector3 currentPos, Vector3 newPos)
    // Returns false if: unwalkable, too steep, or drop too deep

// Alternative path finding
public static bool TryValidateMove(Vector3 currentPos, Vector3 desiredPos, out Vector3 validatedPos)
    // Tries direct path first, then 45-degree offsets

// Position correction
public static Vector3 SnapToTerrain(Vector3 position)
    // Returns position with Y corrected to terrain height

// Per-animal positioning
public static Vector3 ComputeAnimalWorldPos(Vector3 herdCenter, Vector3 animalOffset)
    // Calculates absolute world position for an animal
```

**Dependencies:**
- Calls: `TerrainQuery.CanMove()`, `TerrainQuery.GetHeight()`
- Called by: `HerdBrain` (movement validation)

---

### HerdFactory.cs
**Purpose:** Static factory for creating new herds with realistic composition.

**What it does:**
- Creates herds with randomized positions, sex distribution, genetics
- Applies species-specific configuration (size, speed, awareness)
- Generates realistic age distribution (more young than old)
- Assigns unique IDs to each animal

**Key Methods:**
```csharp
// Main factory method
public static HerdBrain CreateHerd(Species species, Vector3 initialCenter, int? rngSeed = null)

// Species configuration
private static SpeciesConfig GetSpeciesConfig(Species species)
    // Returns: BaseAwarenessRadius, FlightDistance, Min/MaxHerdSize, Speeds, etc.

// Distribution helpers
private static float GetFemaleRatio(Species species)
private static void ApplyAgeDistribution(AnimalStruct[] animals, Random rng)
private static Vector3 GeneratePositionInHerd(Vector3 center, float spreadRadius, Random rng)
```

**Dependencies:**
- Calls: `AnimalStruct.Create()`, `AnimalSystem.Instance.GetNextAnimalId()`, `new HerdBrain()`
- Called by: `AnimalRenderer` (test herds), game initialization

---

### GeneticProfile.cs
**Purpose:** Immutable genetic traits for each animal (set at birth, never changes).

**What it does:**
- Defines inheritable traits: horn score, size, coat quality, disease resistance, fertility
- Affects trophy value, visual scale, health, reproduction success

**Key Fields:**
```csharp
public float HornScore;           // 0-1, affects trophy hunting value
public float SizeFactor;          // 0.8-1.2, affects visual scale
public float CoatQuality;         // 0-1, for visual variety
public float DiseaseResistance;   // 0-1, affects health recovery
public float Fertility;           // 0-1, affects reproduction success
```

**Key Methods:**
```csharp
// Factory with species-appropriate ranges
public static GeneticProfile Generate(Species species, AnimalSex sex, Random rng)

// Inheritance (for offspring)
public static GeneticProfile Inherit(GeneticProfile parent1, GeneticProfile parent2, Random rng)
```

**Dependencies:**
- Called by: `AnimalStruct.Create()`, `HerdBrain.SpawnOffspring()`

---

## 🏗️ Building System

### BuildingData.cs
**Purpose:** Pure data class representing a single building instance.

**What it does:**
- Contains NO scene nodes - just simulation data
- Stores: ID, type, position, rotation, chunk, condition, placement time

**Key Fields:**
```csharp
public Guid Id { get; set; }                    // Unique identifier
public BuildingType Type { get; set; }          // Wall, Roof, Door, WaterTrough, etc.
public Vector3 Position { get; set; }
public float Rotation { get; set; }             // Y-axis in radians
public ChunkCoord ChunkCoord { get; set; }
public float Condition { get; set; }            // 0-1 for future degradation
public DateTime PlacedAt { get; set; }
```

**Key Methods:**
```csharp
public BuildingData Clone()  // Deep copy for serialization
```

**Dependencies:**
- Called by: `BuildingSystem` (storage), `BuildingRenderer` (visualization)

---

### BuildingSystem.cs
**Purpose:** Central manager for all buildings (simulation layer).

**What it does:**
- Maintains dictionary of all buildings by GUID
- Tracks buildings per chunk for efficient lookup
- Validates placement (terrain, collisions, bounds)
- Handles costs through `EconomySystem`
- Triggers rendering updates

**Key Methods:**
```csharp
// Singleton
public static BuildingSystem Instance { get; }

// Building management
public bool PlaceBuilding(BuildingType type, Vector3 position, float rotation, out BuildingData building)
public bool RemoveBuilding(Guid buildingId)
public BuildingData GetBuilding(Guid buildingId)
public IReadOnlyList<BuildingData> GetBuildingsInChunk(ChunkCoord coord)

// Validation
public bool CanPlaceBuilding(BuildingType type, Vector3 position, float rotation)
    // Checks: terrain valid, no collisions, within bounds, can afford

// Cost lookup
public float GetBuildingCost(BuildingType type)
```

**Dependencies:**
- Calls: `EconomySystem.Instance.CanAfford()`, `EconomySystem.Instance.SpendMoney()`, `TerrainQuery.GetHeight()`, `BuildingRenderer`
- Called by: `BuildingPlacer` (player actions), game initialization

---

### BuildingPlacer.cs
**Purpose:** Player interaction layer for building placement.

**What it does:**
- Handles build mode (press 'B' to enter/exit)
- Ghost preview that follows mouse cursor
- Rotation (15-degree steps with 'R')
- Visual feedback (green = valid, red = invalid)
- Cost display and placement confirmation

**Key Methods:**
```csharp
// Godot lifecycle
public override void _Ready()
public override void _Process(double delta)
public override void _Input(InputEvent @event)

// Build mode
public void EnterBuildMode(BuildingType type)
public void ExitBuildMode()
public bool IsInBuildMode { get; }

// Placement
private void UpdateGhostPreview()
private void TryPlaceBuilding()
private void RotateBuilding()

// Validation feedback
private void UpdateGhostMaterial(bool isValid)  // Green or red

// Inspector properties
[Export] public Camera3D PlayerCamera { get; set; }
[Export] public float MaxPlacementDistance { get; set; } = 15f;
[Export] public float RotationStep { get; set; } = 15f;
```

**Dependencies:**
- Calls: `BuildingSystem.Instance.CanPlaceBuilding()`, `BuildingSystem.Instance.PlaceBuilding()`, `TerrainQuery.GetHeight()`
- Called by: Player input, UI buttons

---

### BuildingRenderer.cs
**Purpose:** Visual layer for buildings using MultiMeshInstance3D.

**What it does:**
- Renders buildings using one MultiMesh per type per chunk
- Creates distinct colored materials for each building type
- Integrates with chunk streaming for load/unload
- Maintains cache of rendered meshes

**Key Methods:**
```csharp
// Godot lifecycle
public override void _Ready()

// Rendering
public void RenderBuildingsForChunk(ChunkCoord coord, IEnumerable<BuildingData> buildings)
public void ClearBuildingsForChunk(ChunkCoord coord)
public void UpdateBuildingVisual(BuildingData building)

// Materials
private void InitializeMaterials()
private StandardMaterial3D GetMaterialForType(BuildingType type)
    // Wall=beige, Roof=reddish-brown, WaterTrough=blue, etc.

// Singleton
public static BuildingRenderer Instance { get; }
```

**Dependencies:**
- Calls: `BuildingData` properties, `WorldChunkStreamer` (chunk events)
- Called by: `BuildingSystem` (placement/removal), `WorldChunk` (chunk load/unload)

---

## 💰 Economy System

### EconomySystem.cs
**Purpose:** Central financial management (the "bank").

**What it does:**
- Tracks cash balance, monthly revenue/expenses
- Processes spending with `CanAfford()` checks
- Handles revenue streams: trophy hunting, tourism, game sales, carbon credits, research
- Handles costs: staff wages, maintenance, fuel, permits
- Generates monthly financial reports
- Loan processing

**Key Methods:**
```csharp
// Singleton
public static EconomySystem Instance { get; }

// Balance queries
public bool CanAfford(float amount)
public float GetBalance()

// Spending
public bool SpendMoney(float amount, string description = "")
public bool SpendMoneyIfAble(float amount, string description)

// Revenue
public void AddRevenue(float amount, RevenueCategory category, string description)
public enum RevenueCategory { TrophyHunting, Tourism, GameSales, CarbonCredits, Research, Grants }

// Monthly processing
public void ProcessMonthlyEconomy(GameDate currentDate)
private void ProcessMonthlyRevenue()
private void ProcessMonthlyExpenses()

// Loans
public bool TakeOutLoan(Loan loan)
public void ProcessLoanPayments()

// Events
public event Action<GameDate> OnMonthlyStatementReady;
public event Action<float> OnCashBalanceChanged;

// Statistics
public float MonthlyRevenue { get; }
public float MonthlyExpenses { get; }
public float NetIncome => MonthlyRevenue - MonthlyExpenses;
```

**Dependencies:**
- Calls: `GameState.Instance.CashBalance`, `StaffSystem.Instance.GetMonthlyWages()`, `AnimalSystem.Instance.TotalAnimalCount`
- Called by: `TimeSystem` (monthly), `BuildingSystem` (placement), `BuildingPlacer` (player actions)

---

### Transaction.cs
**Purpose:** Data record for a single financial transaction.

**Key Fields:**
```csharp
public float Amount;
public TransactionType Type;        // Income or Expense
public TransactionCategory Category; // Hunting, Wages, Maintenance, etc.
public string Description;
public GameDate Date;
public string ReferenceId;
```

---

### Loan.cs
**Purpose:** Represents a farm loan.

**Key Fields:**
```csharp
public Guid Id;
public string LenderName;
public float PrincipalAmount;
public float InterestRate;          // Annual percentage
public int TermMonths;
public float MonthlyPayment;
public float RemainingBalance;
public int PaymentsRemaining;
public List<Transaction> PaymentHistory;
```

**Key Methods:**
```csharp
public bool ProcessPayment(out Transaction transaction)
public float CalculateTotalInterest()
```

---

## ⏰ Time & Seasons System

### TimeSystem.cs
**Purpose:** Game clock and central heartbeat coordinator.

**What it does:**
- Manages in-game time passage (can be sped up)
- Day/night cycle coordination
- Emits time-based events: daily, monthly, yearly ticks
- Tells other systems when to update

**Key Methods:**
```csharp
// Godot lifecycle
public override void _Ready()
public override void _Process(double delta)

// Time control
public void SetTimeScale(float scale)  // 1.0 = normal, 5.0 = 5x speed
public float CurrentTimeScale { get; }

// Time queries
public GameDate CurrentDate { get; }
public float TimeOfDay { get; }        // 0.0 to 24.0 hours
public Season CurrentSeason { get; }

// Event emitters
private void OnDayTick()
private void OnMonthTick()
private void OnYearTick()

// Events
public event Action OnDayTicked;
public event Action OnMonthTicked;
public event Action OnYearTicked;
public event Action<Season> OnSeasonChanged;
```

**Dependencies:**
- Calls: `AnimalSystem.Instance.OnDailyTick()`, `AnimalSystem.Instance.OnMonthlyTick()`, `EconomySystem.Instance.ProcessMonthlyEconomy()`, `WaterSystem.Instance.OnMonthlyTick()`
- Called by: Godot engine (auto)

---

### GameDate.cs
**Purpose:** Calendar system with South African seasons.

**Key Fields:**
```csharp
public int Day { get; set; }
public int Month { get; set; }     // 1-12
public int Year { get; set; }
public Season Season { get; }       // Auto-calculated from month
```

**Key Methods:**
```csharp
public void AdvanceDays(int days)
public void AdvanceMonths(int months)
public string ToDisplayString()     // "15 January 2026"
public static Season GetSeasonForMonth(int month)
    // Summer: Dec-Feb, Autumn: Mar-May, Winter: Jun-Aug, Spring: Sep-Nov
```

---

### Season.cs
**Purpose:** Enum defining South African seasons.

```csharp
public enum Season
{
    Summer,   // Dec-Feb: Hot, thunderstorms, high evaporation
    Autumn,   // Mar-May: Cooling, occasional rain
    Winter,   // Jun-Aug: Dry, cool, low evaporation
    Spring    // Sep-Nov: Warming, occasional showers
}
```

---

### SeasonalEvent.cs
**Purpose:** Represents a specific seasonal occurrence.

**Key Fields:**
```csharp
public SeasonalEventType Type;      // FirstRains, DroughtWarning, FireSeason, BreedingSeason
public GameDate StartDate;
public GameDate EndDate;
public float Probability;
public Species AffectedSpecies;
public float EconomicImpact;
public string Description;
```

---

### SeasonalEventCalendar.cs
**Purpose:** Manages all seasonal events for the year.

**Key Methods:**
```csharp
public void GenerateYearlyEvents(int year)
public List<SeasonalEvent> GetActiveEvents(GameDate date)
public List<SeasonalEvent> GetUpcomingEvents(GameDate date, int daysAhead)

// Events
public event Action<SeasonalEvent> OnEventStarted;
public event Action<SeasonalEvent> OnEventEnded;
```

---

## 🌍 Terrain & World Streaming

### TerrainGenerator.cs
**Purpose:** Static helper for generating procedural terrain meshes.

**What it does:**
- Uses FastNoiseLite for Perlin noise height generation
- Creates flat-shaded, low-poly meshes
- South African bushveld aesthetic with warm earth tones
- Thread-safe (called from background threads)

**Key Methods:**
```csharp
// Main generation
public static ArrayMesh GenerateTerrainMesh(ChunkCoord coord, float chunkSize)
public static ArrayMesh GenerateTerrainMesh(Vector3 worldOrigin, float chunkSize)

// Height sampling
public static float GetHeightAt(Vector3 worldPos)
public static float GetHeightAt(float x, float z)

// Color generation
private static Color GetVertexColor(float height, float moisture)
    // Low = warm earth tones, High = rock/grass colors

// Constants
private const float HEIGHT_SCALE = 25f;         // Max elevation variation
private const float NOISE_FREQUENCY = 0.003f;   // Terrain feature scale
private const int TERRAIN_RESOLUTION = 128;     // Vertices per chunk side
```

**Dependencies:**
- Called by: `WorldChunk.BuildChunkContent()` (background thread)

---

### WorldChunkStreamer.cs
**Purpose:** Chunk loading/unloading manager (the "streamer").

**What it does:**
- Maintains 3x3 grid of active chunks around player
- Loads new chunks entering grid, unloads chunks leaving
- Uses background threads for generation (no frame drops)
- Coordinates terrain, flora, and building loading

**Key Methods:**
```csharp
// Godot lifecycle
public override void _Ready()
public override void _Process(double delta)

// Chunk management
private void UpdateChunkGrid(Vector3 playerPos)
private void LoadChunk(ChunkCoord coord)
private void UnloadChunk(ChunkCoord coord)
private void ProcessChunkQueue()

// Background generation
private void GenerateChunkAsync(ChunkCoord coord)
private void ApplyChunkBuildResult(ChunkBuildResult result)  // Main thread

// Queries
public WorldChunk GetChunk(ChunkCoord coord)
public bool IsChunkLoaded(ChunkCoord coord)
public IEnumerable<WorldChunk> GetLoadedChunks()

// Settings
[Export] public float ChunkSize { get; set; } = 256f;
[Export] public int GridRadius { get; set; } = 1;  // 1 = 3x3 grid
```

**Dependencies:**
- Calls: `WorldChunk.Initialize()`, `WorldChunk.BuildChunkContent()`, `TerrainGenerator.GenerateTerrainMesh()`, `FloraSystem.Instance.GetFloraForChunk()`, `BuildingRenderer.RenderBuildingsForChunk()`
- Called by: Godot engine (auto)

---

### WorldChunk.cs
**Purpose:** Represents a single 256m x 256m terrain chunk.

**What it does:**
- Contains terrain mesh, flora MultiMeshes, buildings container
- Manages lifecycle: generation, activation, deactivation, disposal
- All visual nodes freed on unload (simulation data persists elsewhere)

**Key Methods:**
```csharp
// Initialization
public void Initialize(ChunkCoord coord, float chunkSize = 256f)

// Background generation (called on background thread)
public ChunkBuildResult BuildChunkContent()

// Main thread application
public void ApplyBuildResult(ChunkBuildResult result)

// Lifecycle
public void Activate()
public void Deactivate()
public void Unload()

// Components
public void SetTerrainMesh(ArrayMesh mesh)
public void AddFloraMultiMesh(FloraType type, MultiMesh multiMesh)
public void ClearFlora()

// Queries
public ChunkCoord Coordinate { get; }
public bool IsLoaded { get; }
public Vector3 WorldOrigin { get; }
public Bounds3D Bounds { get; }
```

**Dependencies:**
- Calls: `TerrainGenerator.GenerateTerrainMesh()`, `FloraSystem.Instance.GetFloraForChunk()`, `FloraPopulator.CreateFloraMultiMeshes()`
- Called by: `WorldChunkStreamer` (lifecycle management)

---

### ChunkCoord.cs
**Purpose:** Struct representing chunk grid coordinates.

**Key Fields:**
```csharp
public int X;  // Grid X coordinate
public int Z;  // Grid Z coordinate
```

**Key Methods:**
```csharp
// Conversion
public Vector3 GetWorldOrigin(float chunkSize)
public static ChunkCoord FromWorldPosition(Vector3 worldPos, float chunkSize)

// Neighbors
public ChunkCoord North => new ChunkCoord(X, Z - 1);
public ChunkCoord South => new ChunkCoord(X, Z + 1);
public ChunkCoord East => new ChunkCoord(X + 1, Z);
public ChunkCoord West => new ChunkCoord(X - 1, Z);
public IEnumerable<ChunkCoord> GetNeighbors()

// Distance
public int DistanceTo(ChunkCoord other) => Math.Abs(X - other.X) + Math.Abs(Z - other.Z);

// Hashing (for Dictionary keys)
public override int GetHashCode() => HashCode.Combine(X, Z);
```

---

### TerrainQuery.cs
**Purpose:** Static utility for efficient terrain height queries.

**Key Methods:**
```csharp
// Height queries
public static float GetHeight(float x, float z)
public static float GetHeight(Vector3 position)

// Validation
public static bool CanMove(Vector3 from, Vector3 to)
    // Checks: valid terrain, slope < max, drop < max

// Batch queries
public static float[] GetHeights(Vector2[] positions)

// Settings
public static float MaxWalkableSlope { get; set; } = 45f;  // Degrees
public static float MaxDropHeight { get; set; } = 5f;      // Meters
```

**Dependencies:**
- Called by: `AnimalMovement`, `BuildingSystem`, `BuildingPlacer`, `HerdBrain`

---

## 💧 Water System

### WaterSystem.cs
**Purpose:** Central water management for the farm.

**What it does:**
- Manages all water sources (dams, rivers, troughs)
- Simulates rainfall, evaporation (seasonal rates), animal consumption
- Tracks drought conditions
- Provides query interface for animals seeking water

**Key Methods:**
```csharp
// Singleton
public static WaterSystem Instance { get; }

// Water source management
public WaterSource CreateWaterSource(WaterSourceType type, Vector3 position, float capacity)
public void RemoveWaterSource(int sourceId)
public WaterSource GetWaterSource(int sourceId)
public IReadOnlyList<WaterSource> GetAllWaterSources()

// Queries for animals
public WaterSource FindNearestWaterSource(Vector3 position, float maxDistance = 1000f)
public WaterSource FindWaterSourceWithCapacity(Vector3 position, float minCapacity)
public List<WaterSource> GetWaterSourcesInRange(Vector3 position, float radius)

// Consumption
public bool ConsumeWater(int sourceId, float amount)
public void ReleaseWaterReservation(int sourceId, float amount)

// Simulation
public void OnMonthlyTick()  // Rainfall, evaporation
public void ProcessRainfall(float amountMM)
public void ProcessEvaporation(Season season)

// Drought
public bool IsDrought { get; }
public int DroughtMonths { get; }
public float AverageAnnualRainfallMM { get; set; }

// Events
public event Action<WaterSource> OnWaterLevelCritical;  // < 25%
public event Action<WaterSource> OnWaterDry;
public event Action<WaterSource> OnWaterQualityLow;     // < 0.3
```

**Dependencies:**
- Calls: `GameState.Instance.CurrentWeather`, `TimeSystem.Instance.CurrentSeason`
- Called by: `TimeSystem` (monthly), `HerdBrain` (water seeking)

---

### WaterSource.cs
**Purpose:** Represents a single water source.

**Key Fields:**
```csharp
public int Id;
public WaterSourceType Type;        // Dam, River, Trough, Spring
public Vector3 Position;
public float CurrentCapacity;
public float MaxCapacity;
public float Quality;               // 0-1 (affected by pollution, algae)
public bool IsNatural;
public List<int> CurrentlyDrinkingAnimalIds;
```

**Key Methods:**
```csharp
public float PercentFull => CurrentCapacity / MaxCapacity;
public bool HasCapacity(float amount) => CurrentCapacity >= amount;
public bool Consume(float amount)
public void Replenish(float amount)
```

---

## 🌱 Grazing & Ecology System

### GrazingSystem.cs
**Purpose:** Manages animal grazing behavior and grass consumption.

**What it does:**
- Works with `IGrazingAgent` interface (individuals or herds)
- Tracks which patch each agent is targeting
- Implements patch selection (prefers high biomass)
- Handles consumption rates by species
- Prevents overcrowding at patches

**Key Methods:**
```csharp
// Agent registration
public void RegisterAgent(IGrazingAgent agent)
public void UnregisterAgent(IGrazingAgent agent)

// Per-frame update
public void UpdateAgentGrazing(IGrazingAgent agent, float deltaTime)

// Patch selection
private GrassSpawner SelectBestPatch(Vector3 position, float searchRadius)
private float ScorePatch(GrassSpawner patch, Vector3 agentPos)

// Consumption
private void ConsumeGrass(IGrazingAgent agent, GrassSpawner patch, float amount)

// Movement
private Vector3 CalculateGrazingMoveTarget(IGrazingAgent agent)

// Interface expected
public interface IGrazingAgent
{
    ulong GrazingId { get; }
    Vector3 Position { get; }
    float Hunger { get; }           // 0-1
    float GrazingRate { get; }      // Biomass units per second
    bool CanGraze { get; }          // False if fleeing, sleeping, dead
}
```

**Dependencies:**
- Calls: `IGrassEcologyProvider.GetState()`, `IGrassEcologyProvider.ApplyGrazing()`
- Called by: `HerdBrain` (during Grazing state)

---

### GrassEcologySystem.cs
**Purpose:** Simulates grass growth and biomass across the farm.

**What it does:**
- Divides world into grass patches
- Each patch has biomass (0-100%), growth rate, carrying capacity
- Simulates daily growth (seasonal variation)
- Tracks carrying capacity for farm planning

**Key Methods:**
```csharp
// Singleton
public static GrassEcologySystem Instance { get; }

// Patch queries
public GrassPatch GetPatch(int x, int z)
public GrassPatch GetPatchAtWorldPosition(Vector3 position)
public float GetBiomassAt(Vector3 position)

// Simulation
public void SimulateDay()  // Growth based on moisture, season
public void SetMoisture(Vector3 position, float moisture)

// Carrying capacity
public float CalculateCarryingCapacity(Vector3 center, float radius)
    // How many animals can this area support?

// Grazing interface implementation
public class GrassEcologyProvider : IGrassEcologyProvider
{
    public GrassPatchState GetState(GrassSpawner spawner)
    public void ApplyGrazing(GrassSpawner spawner, float amount)
}
```

---

### IGrassEcologyProvider.cs
**Purpose:** Interface decoupling grazing from ecology implementation.

```csharp
public interface IGrassEcologyProvider
{
    GrassPatchState GetState(GrassSpawner spawner);
    void ApplyGrazing(GrassSpawner spawner, float amount);
}

public readonly struct GrassPatchState
{
    public readonly float Biomass;      // 0-100%
    public readonly bool IsGrazeable;
    public readonly bool IsDepleted;
}
```

---

### GrassSpawner.cs
**Purpose:** Visual grass rendering using MultiMeshInstance3D.

**Key Methods:**
```csharp
// Godot lifecycle
public override void _Ready()

// Density control
public void UpdateDensity(float biomassPercentage)
    // More biomass = more grass blades rendered

// Wind animation
public void SetWindParams(float speed, float direction)
```

---

## 🌿 Flora System (Trees & Vegetation)

### FloraSystem.cs
**Purpose:** Global singleton managing all trees and large vegetation.

**What it does:**
- Spatial indexing: chunk → list of flora
- Thread-safe with reader-writer locks
- Lazy-loads flora data (generates procedurally on first request)
- Manages 10,000 hectare world

**Key Methods:**
```csharp
// Singleton
public static FloraSystem Instance { get; }

// Chunk queries (thread-safe)
public List<FloraEntry> GetFloraForChunk(ChunkCoord coord)
public bool HasFloraForChunk(ChunkCoord coord)

// Procedural generation
private List<FloraEntry> GenerateFloraForChunk(ChunkCoord coord)
    // Uses noise for species distribution, density

// Global queries
public int TotalFloraCount { get; }
public List<FloraEntry> GetFloraInRadius(Vector3 center, float radius)

// Species distribution
private FloraType SelectSpeciesForConditions(float height, float moisture, float slope)
```

---

### FloraEntry.cs
**Purpose:** Data for a single tree or large plant.

**Key Fields:**
```csharp
public FloraType Species;       // Acacia, Marula, Baobab, etc.
public Vector3 Position;
public float Scale;
public float Health;            // 0-1
public float Age;               // In years
public ChunkCoord ChunkCoord;
```

---

### FloraPopulator.cs
**Purpose:** Renders trees using MultiMeshInstance3D.

**Key Methods:**
```csharp
// MultiMesh creation
public static Dictionary<FloraType, MultiMesh> CreateFloraMultiMeshes(
    List<FloraEntry> entries, 
    ChunkCoord coord, 
    float chunkSize)

// Variety without unique meshes
private static void ApplyVariation(MultiMeshInstance3D instance, FloraEntry entry)
    // Scale, slight color variation via shader params
```

---

## 👥 Staff System

### StaffSystem.cs
**Purpose:** Manages farm staff (rangers, guides, mechanics, vets, laborers).

**What it does:**
- Tracks staff members, roles, salaries, skills, morale
- Handles hiring, firing, wage payments
- Work scheduling and task assignment

**Key Methods:**
```csharp
// Singleton
public static StaffSystem Instance { get; }

// Staff management
public StaffMember HireStaff(StaffRole role, string name, float skillLevel)
public bool FireStaff(int staffId)
public StaffMember GetStaff(int staffId)
public IReadOnlyList<StaffMember> GetAllStaff()
public IReadOnlyList<StaffMember> GetStaffByRole(StaffRole role)

// Finances
public float GetMonthlyWages()
public void ProcessMonthlyPayroll()

// Task assignment
public void AssignTask(int staffId, StaffTask task)
public void CompleteTask(int staffId)
public StaffMember FindAvailableStaff(StaffRole role)

// Events
public event Action<StaffMember> OnStaffHired;
public event Action<StaffMember> OnStaffFired;
```

**Dependencies:**
- Calls: `EconomySystem.Instance.SpendMoney()` (wages)
- Called by: `EconomySystem` (monthly payroll), Player UI

---

### StaffMember.cs
**Purpose:** Data for a single staff member.

**Key Fields:**
```csharp
public int Id;
public string Name;
public StaffRole Role;          // Ranger, Guide, Mechanic, Veterinarian, Laborer
public float SkillLevel;        // 0-1, improves over time
public float Salary;            // Monthly in ZAR
public float Morale;            // 0-1, affects performance
public StaffTask CurrentTask;
public Vector3 CurrentLocation;
public int YearsOfService;
```

**Key Methods:**
```csharp
public void ImproveSkill(float amount)
public void AdjustMorale(float delta)
public float CalculateEffectiveness()
    // Based on skill, morale, role
```

---

## ☁️ Weather & Environment

### WeatherSystem.cs
**Purpose:** Simulates weather patterns with South African characteristics.

**What it does:**
- Tracks: current weather, temperature, wind, rainfall
- Seasonal weather probabilities (summer thunderstorms, dry winters)
- Affects other systems (water, animal behavior)

**Key Methods:**
```csharp
// Singleton
public static WeatherSystem Instance { get; }

// Current state
public WeatherState CurrentWeather { get; }  // Clear, Cloudy, Rain, Storm, Drought
public float Temperature { get; }            // Celsius
public float WindSpeed { get; }
public float WindDirection { get; }          // Degrees
public float CurrentRainfallMM { get; }

// Simulation
public void SimulateDay()
private WeatherState DetermineNextWeather()
    // Based on season, probabilities

// Seasonal patterns
private float GetRainProbabilityForSeason(Season season)
    // Summer: 60%, Winter: 10%

// Events
public event Action<WeatherState, WeatherState> OnWeatherChanged;
public event Action<float> OnRainfall;  // Amount in mm
```

**Dependencies:**
- Calls: `TimeSystem.Instance.CurrentSeason`, `GameState.Instance.CurrentWeather`
- Called by: `TimeSystem` (daily), `WaterSystem` (rainfall processing)

---

### DayNightCycle.cs
**Purpose:** Controls visual day/night cycle.

**Key Methods:**
```csharp
// Godot lifecycle
public override void _Process(double delta)

// Time control
public void SetTime(float hour)  // 0.0 to 24.0
public float CurrentTime { get; }

// Visual updates
private void UpdateSunPosition(float hour)
private void UpdateAmbientLight(float hour)
private void UpdateSkyShader(float hour)

// Lighting
public float DaylightIntensity { get; }  // 0-1
public bool IsNight => CurrentTime < 6f || CurrentTime > 18f;
```

---

## 🔒 Fence System

### FenceSystem.cs
**Purpose:** Manages farm fences (boundaries, camps, enclosures).

**What it does:**
- Tracks fence segments and their condition
- Validates animal movement (prevents walking through fences)
- Handles degradation and repair costs

**Key Methods:**
```csharp
// Singleton
public static FenceSystem Instance { get; }

// Fence management
public void AddFenceSegment(Vector3 start, Vector3 end, FenceType type)
public void RemoveFenceSegment(int segmentId)
public void RepairFenceSegment(int segmentId)

// Validation
public bool CanAnimalCross(Vector3 from, Vector3 to, AnimalSize size)
    // Checks if path intersects any fence (unless gate is open)

// Queries
public List<FenceSegment> GetFencesInChunk(ChunkCoord coord)
public float GetTotalFenceLength()

// Degradation
public void ProcessMonthlyDegradation()
public float GetRepairCost(int segmentId)

// Gates
public void OpenGate(int gateId)
public void CloseGate(int gateId)
public bool IsGateOpen(int gateId)
```

**Dependencies:**
- Calls: `EconomySystem.Instance.SpendMoney()` (repairs)
- Called by: `AnimalMovement` (validation), `TimeSystem` (monthly degradation)

---

## 🎮 Core Game Systems

### GameState.cs
**Purpose:** Central data store for all saveable game state.

**What it does:**
- Contains all data that needs to persist between sessions
- Other systems read from/write to GameState
- Provides save/load serialization

**Key Fields:**
```csharp
// Singleton
public static GameState Instance { get; }

// Financial
public float CashBalance { get; set; } = 2500000f;  // Starting: R2.5M
public float MonthlyBurnRate { get; private set; }
public float MonthlyRevenue { get; private set; }
public List<Transaction> TransactionHistory { get; }
public List<Loan> ActiveLoans { get; }

// Time
public float TimeOfDay { get; set; } = 8.0f;  // Start at 8 AM
public WeatherState CurrentWeather { get; set; } = WeatherState.Clear;

// World
[Export] public float MapSizeX { get; set; } = 2048f;
[Export] public float MapSizeZ { get; set; } = 2048f;
[Export] public int WorldSeed { get; set; } = 12345;

// Collections (persisted)
public List<BuildingData> Buildings { get; }
public List<WaterSourceData> WaterSources { get; }
public List<StaffMemberData> Staff { get; }
public List<HerdData> Herds { get; }
```

**Key Methods:**
```csharp
// Save/Load
public void SaveGame(string saveName)
public static GameState LoadGame(string saveName)

// Events
[Signal] public delegate void WeatherChangedEventHandler(WeatherState newWeather, WeatherState oldWeather);
```

**Dependencies:**
- Called by: Nearly all systems (read/write state)

---

### BehaviourLOD.cs
**Purpose:** Defines Level of Detail tiers for animal behavior.

```csharp
public enum BehaviourLOD
{
    Full,        // 0-150m:   All needs, awareness, animations active
    Reduced,     // 150-500m: State changes only, simplified animation
    Minimal,     // 500m-2km: Position updated once/second
    Simulation   // 2km+:     Monthly tick only, no world position
}

public static class BehaviourLODExtensions
{
    public static float GetUpdateInterval(this BehaviourLOD lod)
    {
        return lod switch
        {
            BehaviourLOD.Full => 0f,        // Every frame
            BehaviourLOD.Reduced => 0.1f,   // 10 times/second
            BehaviourLOD.Minimal => 1f,     // Once/second
            BehaviourLOD.Simulation => -1f, // Only on scheduled ticks
        };
    }
}
```

---

## Dependency Graph

### Visual Dependency Map

```
┌──────────────────────────────────────────────────────────────────────────────┐
│                              DEPENDENCY GRAPH                                │
└──────────────────────────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────────────────────────┐
│                              CORE LAYER                                      │
│  ┌──────────┐  ┌──────────┐  ┌──────────┐  ┌──────────┐                     │
│  │GameState │  │TimeSystem│  │GameDate  │  │Season    │                     │
│  │ (data)   │  │(heartbeat│  │(calendar)│  │(enum)    │                     │
│  └────┬─────┘  └────┬─────┘  └──────────┘  └──────────┘                     │
│       ▲             │                                                        │
│       └─────────────┘                                                        │
└─────────────────────────────────────────────────────────────────────────────┘
                                       │
                                       ▼
┌─────────────────────────────────────────────────────────────────────────────┐
│                           SYSTEM LAYER                                      │
│                                                                             │
│  ┌─────────────────────────────────────────────────────────────────────┐   │
│  │                        ANIMAL SYSTEM                                │   │
│  │  ┌──────────┐    ┌──────────┐    ┌──────────┐    ┌──────────┐      │   │
│  │  │AnimalSys │───▶│HerdBrain │───▶│AnimalStr │◀───│HerdFact  │      │   │
│  │  │(manager) │    │ (AI)     │    │ (data)   │    │(factory) │      │   │
│  │  └────┬─────┘    └────┬─────┘    └──────────┘    └──────────┘      │   │
│  │       │               │                                             │   │
│  │       │         ┌─────┴─────┐                                       │   │
│  │       │         ▼           ▼                                       │   │
│  │       │    ┌─────────┐  ┌──────────┐                               │   │
│  │       │    │Genetic  │  │AnimalMove│                               │   │
│  │       │    │Profile  │  │ment      │                               │   │
│  │       │    └─────────┘  └────┬─────┘                               │   │
│  │       │                      │                                      │   │
│  │       │    ┌─────────────────┘                                     │   │
│  │       │    ▼                                                        │   │
│  │       │ ┌──────────┐                                               │   │
│  │       └▶│AnimalRend│                                               │   │
│  │         │(visuals) │                                               │   │
│  │         └──────────┘                                               │   │
│  └─────────────────────────────────────────────────────────────────────┘   │
│                                                                             │
│  ┌─────────────────────────────────────────────────────────────────────┐   │
│  │                       BUILDING SYSTEM                               │   │
│  │  ┌──────────┐    ┌──────────┐    ┌──────────┐    ┌──────────┐      │   │
│  │  │Building  │◀───│Building  │◀───│Building  │    │Building  │      │   │
│  │  │System    │    │Placer    │    │Data      │    │Renderer  │      │   │
│  │  │(manager) │    │(player)  │    │(data)    │    │(visuals) │      │   │
│  │  └────┬─────┘    └──────────┘    └──────────┘    └──────────┘      │   │
│  │       │                                                             │   │
│  │       └────────────────────────────────────────────────────────┐   │   │
│  │                                                                │   │   │
│  └────────────────────────────────────────────────────────────────┼───┘   │
│                                                                   │       │
│  ┌────────────────────────────────────────────────────────────────┼───┐   │
│  │                        ECONOMY SYSTEM                          │   │   │
│  │  ┌──────────┐    ┌──────────┐    ┌──────────┐                  │   │   │
│  │  │EconomySys│◀───│Transact  │◀───│Loan      │◀─────────────────┘   │   │
│  │  │(bank)    │    │(record)  │    │(debt)    │                      │   │
│  │  └──────────┘    └──────────┘    └──────────┘                      │   │
│  └────────────────────────────────────────────────────────────────────┘   │
│                                                                             │
│  ┌─────────────────────────────────────────────────────────────────────┐   │
│  │                        WORLD STREAMING                              │   │
│  │  ┌──────────┐    ┌──────────┐    ┌──────────┐    ┌──────────┐      │   │
│  │  │WorldChunk│───▶│ Terrain  │    │ChunkCoord│    │Terrain   │      │   │
│  │  │Streamer  │    │Generator │    │(struct)  │    │Query     │      │   │
│  │  │(manager) │    │(meshes)  │    │          │    │(height)  │      │   │
│  │  └────┬─────┘    └──────────┘    └──────────┘    └──────────┘      │   │
│  │       │                                                             │   │
│  │       └────▶ WorldChunk (instance)                                  │   │
│  └─────────────────────────────────────────────────────────────────────┘   │
│                                                                             │
│  ┌─────────────────────────────────────────────────────────────────────┐   │
│  │                        SUPPORT SYSTEMS                              │   │
│  │  ┌──────────┐    ┌──────────┐    ┌──────────┐    ┌──────────┐      │   │
│  │  │WaterSys  │    │GrazingSys│    │GrassEco  │    │GrassSpaw │      │   │
│  │  │(dams)    │    │(feeding) │    │(growth)  │    │(visuals) │      │   │
│  │  └──────────┘    └────┬─────┘    └────┬─────┘    └──────────┘      │   │
│  │                       │               │                              │   │
│  │                       └───────┬───────┘                              │   │
│  │                               ▼                                      │   │
│  │                       ┌──────────┐                                   │   │
│  │                       │IGrassEco │                                   │   │
│  │                       │Provider  │                                   │   │
│  │                       │(interface)│                                   │   │
│  │                       └──────────┘                                   │   │
│  │                                                                      │   │
│  │  ┌──────────┐    ┌──────────┐    ┌──────────┐                       │   │
│  │  │FloraSys  │───▶│FloraEntry│───▶│FloraPop │                       │   │
│  │  │(trees)   │    │(data)    │    │(visuals)│                       │   │
│  │  └──────────┘    └──────────┘    └──────────┘                       │   │
│  │                                                                      │   │
│  │  ┌──────────┐    ┌──────────┐    ┌──────────┐                       │   │
│  │  │StaffSys  │───▶│StaffMem  │    │FenceSys  │                       │   │
│  │  │(workers) │    │(data)    │    │(barriers)│                       │   │
│  │  └──────────┘    └──────────┘    └──────────┘                       │   │
│  │                                                                      │   │
│  │  ┌──────────┐    ┌──────────┐                                       │   │
│  │  │WeatherSys│───▶│DayNight  │                                       │   │
│  │  │(climate) │    │Cycle     │                                       │   │
│  │  └──────────┘    └──────────┘                                       │   │
│  └─────────────────────────────────────────────────────────────────────┘   │
│                                                                             │
└─────────────────────────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────────────────────────┐
│                            DEPENDENCY TABLE                                 │
├─────────────────────────────────────────────────────────────────────────────┤
│ Script              │ Depends On                                              │
├─────────────────────┼─────────────────────────────────────────────────────────┤
│ AnimalSystem        │ HerdBrain, GameState, TimeSystem                        │
│ HerdBrain           │ AnimalStruct, AnimalMovement, TerrainQuery, WaterSystem │
│ AnimalStruct        │ GeneticProfile                                          │
│ AnimalRenderer      │ AnimalSystem, HerdBrain, AnimalStruct                   │
│ AnimalMovement      │ TerrainQuery                                            │
│ HerdFactory         │ AnimalStruct, AnimalSystem, HerdBrain                   │
│ GeneticProfile      │ (none - pure data)                                      │
├─────────────────────┼─────────────────────────────────────────────────────────┤
│ BuildingSystem      │ BuildingData, EconomySystem, TerrainQuery               │
│ BuildingPlacer      │ BuildingSystem, TerrainQuery                            │
│ BuildingData        │ ChunkCoord                                              │
│ BuildingRenderer    │ BuildingData, WorldChunkStreamer                        │
├─────────────────────┼─────────────────────────────────────────────────────────┤
│ EconomySystem       │ GameState, Transaction, Loan, StaffSystem               │
│ Transaction         │ GameDate                                                │
│ Loan                │ Transaction, GameDate                                   │
├─────────────────────┼─────────────────────────────────────────────────────────┤
│ TimeSystem          │ GameDate, Season, AnimalSystem, EconomySystem           │
│ GameDate            │ Season                                                  │
│ SeasonalEvent       │ GameDate, Season                                        │
│ SeasonalEventCal    │ SeasonalEvent, GameDate                                 │
├─────────────────────┼─────────────────────────────────────────────────────────┤
│ WorldChunkStreamer  │ WorldChunk, TerrainGenerator, FloraSystem               │
│ WorldChunk          │ TerrainGenerator, FloraSystem, FloraPopulator           │
│ TerrainGenerator    │ ChunkCoord                                              │
│ TerrainQuery        │ (none - static utility)                                 │
│ ChunkCoord          │ (none - struct)                                         │
├─────────────────────┼─────────────────────────────────────────────────────────┤
│ WaterSystem         │ WaterSource, GameState, TimeSystem, Season              │
│ WaterSource         │ (none - data)                                           │
├─────────────────────┼─────────────────────────────────────────────────────────┤
│ GrazingSystem       │ IGrassEcologyProvider, GrassSpawner                     │
│ GrassEcologySystem  │ IGrassEcologyProvider, GrassSpawner, TimeSystem         │
│ GrassEcologyAdapter │ GrassEcologySystem, GrazingSystem                       │
│ GrassSpawner        │ (none - visual)                                         │
├─────────────────────┼─────────────────────────────────────────────────────────┤
│ FloraSystem         │ FloraEntry, ChunkCoord                                  │
│ FloraEntry          │ ChunkCoord                                              │
│ FloraPopulator      │ FloraEntry, FloraType                                   │
├─────────────────────┼─────────────────────────────────────────────────────────┤
│ StaffSystem         │ StaffMember, EconomySystem                              │
│ StaffMember         │ (none - data)                                           │
├─────────────────────┼─────────────────────────────────────────────────────────┤
│ WeatherSystem       │ Season, GameState, TimeSystem                           │
│ DayNightCycle       │ TimeSystem                                              │
├─────────────────────┼─────────────────────────────────────────────────────────┤
│ FenceSystem         │ EconomySystem                                           │
├─────────────────────┼─────────────────────────────────────────────────────────┤
│ GameState           │ (none - central data store)                             │
│ BehaviourLOD        │ (none - enum)                                           │
└─────────────────────┴─────────────────────────────────────────────────────────┘
```

### Call Flow Examples

**1. Animal Spawning Flow:**
```
Game Init / Player Action
    │
    ▼
HerdFactory.CreateHerd(species, position, seed)
    │
    ├──▶ SpeciesConfig config = GetSpeciesConfig(species)
    │
    ├──▶ AnimalStruct[] animals = new AnimalStruct[herdSize]
    │       │
    │       └──▶ AnimalStruct.Create(position, sex, id, rng)
    │               │
    │               └──▶ GeneticProfile.Generate(species, sex, rng)
    │
    ├──▶ ApplyAgeDistribution(animals, rng)
    │
    └──▶ new HerdBrain(species, config, center, animals, seed)
            │
            └──▶ AnimalSystem.Instance.AddHerd(herdBrain)
```

**2. Monthly Game Tick Flow:**
```
TimeSystem._Process()
    │
    └──▶ OnMonthTick()
            │
            ├──▶ AnimalSystem.Instance.OnMonthlyTick()
            │       │
            │       └──▶ For each herd: herd.OnMonthlyTick()
            │               │
            │               ├──▶ ProcessAging()
            │               ├──▶ ProcessDeaths()
            │               └──▶ TryReproduce()
            │
            ├──▶ EconomySystem.Instance.ProcessMonthlyEconomy(date)
            │       │
            │       ├──▶ ProcessMonthlyRevenue()
            │       ├──▶ ProcessMonthlyExpenses()
            │       ├──▶ ProcessLoanPayments()
            │       └──▶ OnMonthlyStatementReady?.Invoke(date)
            │
            ├──▶ WaterSystem.Instance.OnMonthlyTick()
            │       │
            │       ├──▶ ProcessRainfall()
            │       ├──▶ ProcessEvaporation(season)
            │       └──▶ CheckDroughtConditions()
            │
            └──▶ StaffSystem.Instance.ProcessMonthlyPayroll()
                    │
                    └──▶ EconomySystem.Instance.SpendMoney(wages)
```

**3. Building Placement Flow:**
```
Player presses 'B'
    │
    ▼
BuildingPlacer.EnterBuildMode(BuildingType.Wall)
    │
    ├──▶ Create ghost preview mesh
    │
    └──▶ UpdateGhostPreview() [every frame]
            │
            ├──▶ Raycast from camera to terrain
            │
            ├──▶ BuildingSystem.Instance.CanPlaceBuilding(type, pos, rot)
            │       │
            │       ├──▶ TerrainQuery.GetHeight(pos)
            │       ├──▶ Check collisions
            │       └──▶ EconomySystem.Instance.CanAfford(cost)
            │
            └──▶ UpdateGhostMaterial(isValid ? green : red)

Player clicks to place
    │
    ▼
BuildingPlacer.TryPlaceBuilding()
    │
    └──▶ BuildingSystem.Instance.PlaceBuilding(type, pos, rot, out building)
            │
            ├──▶ EconomySystem.Instance.SpendMoney(cost)
            │
            ├──▶ Create BuildingData
            │
            ├──▶ Add to buildings dictionary
            │
            └──▶ BuildingRenderer.RenderBuildingsForChunk(chunk)
```

**4. Animal Grazing Flow:**
```
HerdBrain.Tick() [in Grazing state]
    │
    └──▶ ExecuteGrazing(deltaTime)
            │
            ├──▶ GrazingSystem.UpdateAgentGrazing(this, deltaTime)
            │       │
            │       ├──▶ SelectBestPatch(position, searchRadius)
            │       │       │
            │       │       └──▶ IGrassEcologyProvider.GetState(patch)
            │       │
            │       ├──▶ CalculateGrazingMoveTarget(agent)
            │       │
            │       └──▶ ConsumeGrass(agent, patch, amount)
            │               │
            │               └──▶ IGrassEcologyProvider.ApplyGrazing(patch, amount)
            │
            └──▶ Update animations
```

---

## Quick Reference: Common Tasks

### How do I spawn a new herd?
```csharp
var herd = HerdFactory.CreateHerd(
    Species.Kudu, 
    new Vector3(100f, 0f, 100f), 
    rngSeed: 42
);
AnimalSystem.Instance.AddHerd(herd);
```

### How do I check if the player can afford something?
```csharp
if (EconomySystem.Instance.CanAfford(5000f))
{
    EconomySystem.Instance.SpendMoney(5000f, "Bought a water trough");
}
```

### How do I get terrain height at a position?
```csharp
float height = TerrainQuery.GetHeight(100f, 100f);
// or
float height = TerrainQuery.GetHeight(new Vector3(100f, 0f, 100f));
```

### How do I find the nearest water source?
```csharp
var water = WaterSystem.Instance.FindNearestWaterSource(
    animalPosition, 
    maxDistance: 1000f
);
if (water != null && water.PercentFull > 0.25f)
{
    // Animal can drink here
}
```

### How do I listen for time events?
```csharp
TimeSystem.Instance.OnMonthTicked += () =>
{
    // Do something every game month
};
```

---

## Performance Notes

| Script | Update Frequency | Thread | Key Optimization |
|--------|-----------------|--------|------------------|
| AnimalSystem | Every frame | Main | LOD-aware ticking |
| HerdBrain | Variable (LOD) | Main | Timer-based variation (no per-frame RNG) |
| AnimalRenderer | Every frame | Main | MultiMeshInstance3D (one per species) |
| TerrainGenerator | On demand | Background | Static class, thread-safe |
| WorldChunkStreamer | Every frame | Main + Background | Async chunk generation |
| FloraSystem | On demand | Main + Background | Reader-writer locks |
| EconomySystem | Monthly | Main | Pure data, no scene nodes |
| WaterSystem | Monthly | Main | Event-driven (no polling) |

---

*Generated for BasterBoerv3 - BasterBoer Plaas Speletjie*
