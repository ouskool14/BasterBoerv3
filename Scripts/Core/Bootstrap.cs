using Godot;
using BasterBoer.Core.Systems;
using BasterBoer.Core.Economy;
using BasterBoer.Persistence;

namespace BasterBoer.Core
{
	/// <summary>
	/// Central bootstrap autoload. Initializes all simulation systems in correct
	/// dependency order and provides typed accessors for the rest of the codebase.
	///
	/// Registered as the sole autoload in project.godot. All other systems are
	/// instantiated here, guaranteeing initialization order regardless of scene tree layout.
	/// </summary>
	public partial class Bootstrap : Node
	{
		/// <summary>Singleton instance accessible at /root/Bootstrap.</summary>
		public static Bootstrap Instance { get; private set; }

		// --- Child system nodes created by Bootstrap ---
		private GameState _gameState;
		private TimeSystem _timeSystem;
		private EconomySystem _economySystem;

		// --- Typed accessors (static for convenience) ---

		/// <summary>Global game state (cash, weather, map config).</summary>
		public static GameState Game => Instance?._gameState;

		/// <summary>Game clock and temporal signals.</summary>
		public static TimeSystem Time => Instance?._timeSystem;

		/// <summary>Financial management system.</summary>
		public static EconomySystem Economy => Instance?._economySystem;

		/// <summary>Animal herd simulation (pure C# singleton).</summary>
		public static LandManagementSim.Simulation.AnimalSystem Animals =>
			LandManagementSim.Simulation.AnimalSystem.Instance;

		/// <summary>Staff management (pure C# singleton).</summary>
		public static StaffSystem Staff => StaffSystem.Instance;

		/// <summary>Water source management (pure C# singleton).</summary>
		public static WaterSystem Water => WaterSystem.Instance;

		/// <summary>Fence system (scene node — created in main.tscn, not by Bootstrap).
		/// FenceSystem has [Export] properties requiring editor assignment.</summary>
		public static FenceSystem Fence => FenceSystem.Instance;

		/// <summary>World chunk streamer (scene node — created in main.tscn, not by Bootstrap).
		/// WorldChunkStreamer needs a player NodePath export set in the editor.</summary>
		public static WorldStreaming.WorldChunkStreamer Streamer => WorldStreaming.WorldChunkStreamer.Instance;

		// --- Simulation ticker ---
		private SimulationTicker _simTicker;

		/// <summary>Simulation ticker that dispatches time signals to heavy/light systems.</summary>
		public static SimulationTicker Ticker => Instance?._simTicker;

		/// <summary>
		/// Runs before _Ready(). Registers the singleton so other autoloads
		/// (e.g. WeatherSystem) can find Bootstrap.Instance immediately.
		/// </summary>
		public override void _EnterTree()
		{
			if (Instance != null && Instance != this)
			{
				GD.PrintErr("[Bootstrap] Duplicate instance detected — freeing.");
				QueueFree();
				return;
			}
			Instance = this;
			GD.Print("[Bootstrap] Instance registered.");
		}

		/// <summary>
		/// Creates and attaches all simulation systems as children.
		/// Order is critical: GameState first, then TimeSystem, then everything else.
		/// </summary>
		public override void _Ready()
		{
			GD.Print("[Bootstrap] Initializing systems...");

			// 1. GameState (must exist before everything else)
			_gameState = EnsureNode<GameState>("GameState");
			GD.Print("[Bootstrap] GameState ready.");

			// 2. TimeSystem (needs GameState for UpdateTimeOfDay)
			_timeSystem = EnsureNode<TimeSystem>("TimeSystem");
			GD.Print("[Bootstrap] TimeSystem ready.");

			// 3. EconomySystem (connects to TimeSystem signals in its _Ready)
			_economySystem = EnsureNode<EconomySystem>("EconomySystem");
			GD.Print("[Bootstrap] EconomySystem ready.");

			// 4. Pure C# singletons — force initialization by accessing Instance
			var _ = WaterSystem.Instance;
			GD.Print("[Bootstrap] WaterSystem singleton initialized.");

			var __ = LandManagementSim.Simulation.AnimalSystem.Instance;
			GD.Print("[Bootstrap] AnimalSystem singleton initialized.");

			var ___ = StaffSystem.Instance;
			GD.Print("[Bootstrap] StaffSystem singleton initialized.");

			// 5. Simulation ticker — dispatches TimeSystem signals to all systems
			_simTicker = new SimulationTicker();
			_simTicker.Name = "SimulationTicker";
			AddChild(_simTicker);
			GD.Print("[Bootstrap] SimulationTicker attached.");

			// 6. Save manager — handles persistence
			var saveManager = EnsureNode<SaveManager>("SaveManager");
			GD.Print("[Bootstrap] SaveManager ready.");

			GD.Print("[Bootstrap] All systems initialized successfully.");

			// Wire WorldChunkStreamer to player node (deferred — main scene loads after autoloads)
			CallDeferred(MethodName.WirePlayerToChunkStreamer);
		}

		/// <summary>
		/// Finds the player node in the main scene and wires it to WorldChunkStreamer.
		/// Called deferred because autoloads load before the main scene.
		/// </summary>
		private void WirePlayerToChunkStreamer()
		{
			var player = GetTree().Root.FindChild("Boer", true, false) as Node3D;
			var streamer = WorldStreaming.WorldChunkStreamer.Instance;
			if (player != null && streamer != null)
			{
				streamer.SetPlayerNode(player);
				GD.Print("[Bootstrap] WorldChunkStreamer wired to player node.");
			}
			else
			{
				GD.PrintErr("[Bootstrap] Could not wire WorldChunkStreamer: player or streamer not found.");
			}
		}

		/// <summary>
		/// Creates a child node of the given type if one doesn't already exist with that name.
		/// Prevents duplicates when the same node is also present in the scene file.
		/// </summary>
		private T EnsureNode<T>(string nodeName) where T : Node, new()
		{
			// Check if a node with this name already exists as our child
			var existing = GetNodeOrNull<T>(nodeName);
			if (existing != null)
			{
				GD.Print($"[Bootstrap] Reusing existing {nodeName}.");
				return existing;
			}

			var node = new T();
			node.Name = nodeName;
			AddChild(node);
			GD.Print($"[Bootstrap] Created {nodeName}.");
			return node;
		}

		/// <summary>
		/// Cleanup on tree exit. Clears singleton references so systems don't
		/// hold stale pointers if the scene is reloaded.
		/// </summary>
		public override void _ExitTree()
		{
			GD.Print("[Bootstrap] Shutting down...");

			if (Instance == this)
			{
				Instance = null;
			}

			_simTicker = null;
			_gameState = null;
			_timeSystem = null;
			_economySystem = null;
		}
	}
}
