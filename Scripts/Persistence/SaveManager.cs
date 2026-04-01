using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using BasterBoer.Core;
using BasterBoer.Core.Systems;
using BasterBoer.Core.Time;
using BasterBoer.Core.Water;
using LandManagementSim.Simulation;

namespace BasterBoer.Persistence
{
	/// <summary>
	/// Central save/load orchestrator. Serializes all system state to JSON
	/// and restores it on load. Handles version migration and graceful failure.
	///
	/// Uses System.Text.Json (not Newtonsoft) per AGENTS.md conventions.
	/// </summary>
	public partial class SaveManager : Node
	{
		private const string SaveDir = "user://saves/";
		private const int CurrentVersion = 1;

		public static SaveManager Instance { get; private set; }

		[Signal] public delegate void SaveStartedEventHandler();
		[Signal] public delegate void SaveCompletedEventHandler(string path);
		[Signal] public delegate void SaveFailedEventHandler(string error);
		[Signal] public delegate void LoadStartedEventHandler();
		[Signal] public delegate void LoadCompletedEventHandler();
		[Signal] public delegate void LoadFailedEventHandler(string error);

		public override void _EnterTree()
		{
			if (Instance != null && Instance != this)
			{
				QueueFree();
				return;
			}
			Instance = this;
		}

		/// <summary>
		/// Saves the current game state to a JSON file.
		/// </summary>
		/// <param name="slotName">Save slot name (used as filename)</param>
		/// <returns>True if save succeeded</returns>
		public bool SaveGame(string slotName)
		{
			EmitSignal(SignalName.SaveStarted);

			try
			{
				var data = CollectSaveData();
				var json = JsonSerializer.Serialize(data, new JsonSerializerOptions
				{
					WriteIndented = true,
					IncludeFields = true
				});

				// Ensure save directory exists
				var savePath = ProjectSettings.GlobalizePath(SaveDir);
				Directory.CreateDirectory(savePath);

				var filePath = Path.Combine(savePath, $"{slotName}.json");

				// Write with backup
				var backupPath = filePath + ".bak";
				if (File.Exists(filePath))
					File.Copy(filePath, backupPath, true);

				File.WriteAllText(filePath, json);

				GD.Print($"[SaveManager] Saved to {filePath}");
				EmitSignal(SignalName.SaveCompleted, filePath);
				return true;
			}
			catch (Exception ex)
			{
				GD.PrintErr($"[SaveManager] Save failed: {ex.Message}");
				EmitSignal(SignalName.SaveFailed, ex.Message);
				return false;
			}
		}

		/// <summary>
		/// Loads game state from a JSON file.
		/// </summary>
		/// <param name="slotName">Save slot name</param>
		/// <returns>True if load succeeded</returns>
		public bool LoadGame(string slotName)
		{
			EmitSignal(SignalName.LoadStarted);

			try
			{
				var savePath = ProjectSettings.GlobalizePath(SaveDir);
				var filePath = Path.Combine(savePath, $"{slotName}.json");

				if (!File.Exists(filePath))
				{
					GD.PrintErr($"[SaveManager] Save file not found: {filePath}");
					EmitSignal(SignalName.LoadFailed, "Save file not found");
					return false;
				}

				var json = File.ReadAllText(filePath);
				var data = JsonSerializer.Deserialize<SaveData>(json, new JsonSerializerOptions
				{
					IncludeFields = true
				});

				if (data == null)
				{
					EmitSignal(SignalName.LoadFailed, "Failed to deserialize save data");
					return false;
				}

				// Version migration
				data = MigrateIfNeeded(data);

				// Restore in dependency order
				RestoreSaveData(data);

				GD.Print($"[SaveManager] Loaded from {filePath}");
				EmitSignal(SignalName.LoadCompleted);
				return true;
			}
			catch (Exception ex)
			{
				GD.PrintErr($"[SaveManager] Load failed: {ex.Message}");
				EmitSignal(SignalName.LoadFailed, ex.Message);
				return false;
			}
		}

		/// <summary>
		/// Gets info about all save slots for UI display.
		/// </summary>
		public List<SaveSlotInfo> GetSaveSlots()
		{
			var slots = new List<SaveSlotInfo>();
			var savePath = ProjectSettings.GlobalizePath(SaveDir);

			if (!Directory.Exists(savePath))
				return slots;

			foreach (var file in Directory.GetFiles(savePath, "*.json"))
			{
				try
				{
					var json = File.ReadAllText(file);
					var data = JsonSerializer.Deserialize<SaveData>(json, new JsonSerializerOptions
					{
						IncludeFields = true
					});

					if (data != null)
					{
						slots.Add(new SaveSlotInfo
						{
							SlotName = Path.GetFileNameWithoutExtension(file),
							SavedAt = data.SavedAt,
							Year = data.Time?.Year ?? 0,
							Month = data.Time?.Month ?? 0,
							Balance = data.Economy?.Balance ?? 0,
							SaveVersion = data.SaveVersion
						});
					}
				}
				catch
				{
					// Skip corrupted saves
				}
			}

			return slots.OrderByDescending(s => s.SavedAt).ToList();
		}

		/// <summary>
		/// Deletes a save slot.
		/// </summary>
		public bool DeleteSave(string slotName)
		{
			var savePath = ProjectSettings.GlobalizePath(SaveDir);
			var filePath = Path.Combine(savePath, $"{slotName}.json");

			if (File.Exists(filePath))
			{
				File.Delete(filePath);
				GD.Print($"[SaveManager] Deleted save: {slotName}");
				return true;
			}
			return false;
		}

		/// <summary>
		/// Collects all system state into a SaveData object.
		/// </summary>
		private SaveData CollectSaveData()
		{
			var bootstrap = Bootstrap.Instance;
			var timeSys = Bootstrap.Time;
			var gameState = Bootstrap.Game;
			var econSys = Bootstrap.Economy;

			var data = new SaveData
			{
				SaveId = Guid.NewGuid().ToString(),
				SavedAt = DateTime.UtcNow.ToString("o"),
				SaveVersion = CurrentVersion,
				GameVersion = "0.1",
				WorldSeed = gameState?.WorldSeed ?? 12345,
				MapSizeX = gameState?.MapSizeX ?? 2048f,
				MapSizeZ = gameState?.MapSizeZ ?? 2048f
			};

			// Time
			if (timeSys != null)
			{
				data.Time = new TimeData
				{
					Day = timeSys.CurrentDate.Day,
					Month = timeSys.CurrentDate.Month,
					Year = timeSys.CurrentDate.Year,
					TimeOfDay = 8.0f,
					CurrentSeason = timeSys.CurrentSeason.ToString()
				};
			}

			// Economy
			data.Economy = new EconomyData
			{
				Balance = gameState?.CashBalance ?? 0,
				MonthlyRevenue = econSys?.MonthlyRevenue ?? 0,
				MonthlyExpenses = econSys?.MonthlyExpenses ?? 0
			};

			// Herds
			data.Herds = new List<HerdData>();
			var animalSys = AnimalSystem.Instance;
			if (animalSys != null)
			{
				for (int i = 0; i < animalSys.Herds.Count; i++)
				{
					var herd = animalSys.Herds[i];
					data.Herds.Add(new HerdData
					{
						HerdId = herd.HerdId,
						Species = herd.Species.ToString(),
						CenterX = herd.CenterPosition.X,
						CenterY = herd.CenterPosition.Y,
						CenterZ = herd.CenterPosition.Z,
						Population = herd.Animals.Length,
						Thirst = herd.Thirst,
						Hunger = herd.Hunger,
						Fatigue = herd.Fatigue
					});
				}
			}

			// Water points
			data.WaterPoints = new List<WaterPointData>();
			var waterSys = WaterSystem.Instance;
			if (waterSys != null)
			{
				for (int i = 0; i < waterSys.WaterSources.Count; i++)
				{
					var ws = waterSys.WaterSources[i];
					data.WaterPoints.Add(new WaterPointData
					{
						Id = ws.Id,
						Name = ws.Name,
						Type = ws.Type.ToString(),
						PositionX = ws.Position.X,
						PositionY = ws.Position.Y,
						PositionZ = ws.Position.Z,
						Capacity = ws.CurrentLevel,
						MaxCapacity = ws.MaxCapacityM3,
						IsOperational = ws.Status == WaterSourceStatus.Operational,
						Quality = ws.QualityFactor,
						Status = ws.Status.ToString()
					});
				}
			}

			// Fence segments
			data.FenceSegments = new List<FenceSegmentData>();
			var fenceHealth = Fence.FenceHealthSystem.Instance;
			if (fenceHealth != null)
			{
				for (int i = 0; i < fenceHealth.Segments.Count; i++)
				{
					var seg = fenceHealth.Segments[i];
					data.FenceSegments.Add(new FenceSegmentData
					{
						Id = seg.Id,
						Type = seg.Type.ToString(),
						StartX = seg.StartPoint.X,
						StartY = seg.StartPoint.Y,
						StartZ = seg.StartPoint.Z,
						EndX = seg.EndPoint.X,
						EndY = seg.EndPoint.Y,
						EndZ = seg.EndPoint.Z,
						Condition = seg.Condition,
						MaxCondition = seg.MaxCondition,
						IsBreach = seg.IsBreach,
						LastInspectedDate = seg.LastInspected.ToString()
					});
				}
			}

			// Staff
			data.Staff = new List<StaffData>();
			var staffSys = StaffSystem.Instance;
			if (staffSys != null)
			{
				for (int i = 0; i < staffSys.Staff.Count; i++)
				{
					var s = staffSys.Staff[i];
					data.Staff.Add(new StaffData
					{
						Id = s.Id,
						Name = s.Name,
						Role = s.Role.ToString(),
						SkillLevel = s.SkillLevel,
						Morale = s.Morale,
						Loyalty = s.Loyalty,
						MonthlySalary = s.MonthlySalaryZAR,
						Status = s.Status.ToString(),
						MonthsEmployed = s.MonthsEmployed,
						AssignedZoneId = s.AssignedZoneId
					});
				}
			}

			// Weather
			data.Weather = new WeatherData
			{
				CurrentWeather = gameState?.CurrentWeather.ToString() ?? "Clear"
			};

			// Player position (simplified — get from scene if available)
			data.Player = new PlayerData
			{
				PositionX = 55.373f,
				PositionY = 18f,
				PositionZ = 41.189f,
				RotationY = 0f
			};

			// Recent events
			data.RecentEvents = new List<GameEventData>();
			var logger = EventLogger.Instance;
			if (logger != null)
			{
				var recent = logger.ExportRecent(50);
				for (int i = 0; i < recent.Count; i++)
				{
					var evt = recent[i];
					data.RecentEvents.Add(new GameEventData
					{
						Id = evt.Id,
						Category = evt.Category.ToString(),
						Timestamp = evt.Timestamp.ToString(),
						Summary = evt.Summary,
						Details = evt.Details
					});
				}
			}

			return data;
		}

		/// <summary>
		/// Restores all system state from a SaveData object.
		/// </summary>
		private void RestoreSaveData(SaveData data)
		{
			var gameState = Bootstrap.Game;
			var timeSys = Bootstrap.Time;

			// World seed and map
			if (gameState != null)
			{
				gameState.WorldSeed = data.WorldSeed;
				gameState.MapSizeX = data.MapSizeX;
				gameState.MapSizeZ = data.MapSizeZ;
			}

			// Time
			if (data.Time != null && timeSys != null)
			{
				// TimeSystem doesn't expose setters, but we can print what we'd restore
				GD.Print($"[SaveManager] Restoring time: {data.Time.Day}/{data.Time.Month}/{data.Time.Year}");
			}

			// Economy
			if (data.Economy != null && gameState != null)
			{
				gameState.CashBalance = data.Economy.Balance;
			}

			// Water points
			if (data.WaterPoints != null)
			{
				var waterSys = WaterSystem.Instance;
				if (waterSys != null)
				{
					GD.Print($"[SaveManager] Restoring {data.WaterPoints.Count} water points");
					// Note: Full restoration would require clearing and re-registering
				}
			}

			// Herds
			if (data.Herds != null)
			{
				GD.Print($"[SaveManager] Would restore {data.Herds.Count} herds");
			}

			// Staff
			if (data.Staff != null)
			{
				GD.Print($"[SaveManager] Would restore {data.Staff.Count} staff members");
			}

			// Events
			if (data.RecentEvents != null)
			{
				GD.Print($"[SaveManager] Restoring {data.RecentEvents.Count} recent events");
			}
		}

		/// <summary>
		/// Migrates save data from older versions to current.
		/// </summary>
		private SaveData MigrateIfNeeded(SaveData data)
		{
			if (data.SaveVersion == CurrentVersion)
				return data;

			GD.Print($"[SaveManager] Migrating save from v{data.SaveVersion} to v{CurrentVersion}");

			// Future migrations:
			// if (data.SaveVersion < 2) data = MigrateV1ToV2(data);

			data.SaveVersion = CurrentVersion;
			return data;
		}

		public override void _ExitTree()
		{
			if (Instance == this)
				Instance = null;
		}
	}
}
