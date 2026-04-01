using Godot;
using System;
using WorldStreaming;
using BasterBoer.Core.Economy;

namespace Basterboer.Buildings
{
	/// <summary>
	/// Handles building placement mode with ghost preview, rotation, and validation.
	/// Provides visual feedback and integrates with the building system.
	/// </summary>
	public partial class BuildingPlacer : Node3D
	{
		[Export] public Camera3D PlayerCamera;
		[Export] public float MaxPlacementDistance = 15.0f;
		[Export] public float RotationStep = 15.0f; // Degrees per rotation input

		private bool _isInBuildMode = false;
		private BuildingType _currentBuildingType = BuildingType.Wall;
		private float _currentRotation = 0.0f;

		// Ghost preview components
		private Node3D _ghostPreview;
		private MeshInstance3D _ghostMesh;
		private StandardMaterial3D _validMaterial;
		private StandardMaterial3D _invalidMaterial;
		
		private Vector3 _lastValidPosition = Vector3.Zero;
		private bool _isPlacementValid = false;

		// UI components
		private Control _buildModeUI;
		private Label _buildModeLabel;
		private Label _costLabel;

		public override void _Ready()
		{
			SetupMaterials();
			SetupCamera();
			SetupUI();
			
			GD.Print("[BuildingPlacer] Initialized - Press 'B' to enter build mode");
		}

		/// <summary>
		/// Creates materials for ghost preview visualization
		/// </summary>
		private void SetupMaterials()
		{
			_validMaterial = new StandardMaterial3D
			{
				Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
				AlbedoColor = new Color(0.0f, 1.0f, 0.0f, 0.6f), // Green semi-transparent
				CullMode = BaseMaterial3D.CullModeEnum.Disabled,
				ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded
			};

			_invalidMaterial = new StandardMaterial3D
			{
				Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
				AlbedoColor = new Color(1.0f, 0.0f, 0.0f, 0.6f), // Red semi-transparent
				CullMode = BaseMaterial3D.CullModeEnum.Disabled,
				ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded
			};
		}

		/// <summary>
		/// Sets up camera reference with fallback detection
		/// </summary>
		private void SetupCamera()
		{
			if (PlayerCamera == null)
			{
				// Try to find camera in scene tree
				PlayerCamera = GetViewport().GetCamera3D();
				
				if (PlayerCamera == null)
				{
					// Look for camera in player node
					var player = GetTree().GetFirstNodeInGroup("player");
					if (player != null)
					{
						PlayerCamera = player.GetNodeOrNull<Camera3D>("Camera3D");
					}
				}
			}

			if (PlayerCamera == null)
			{
				GD.PrintErr("[BuildingPlacer] No camera found! Building placement will not work.");
			}
		}

		/// <summary>
		/// Creates UI elements for build mode
		/// </summary>
		private void SetupUI()
		{
			_buildModeUI = new Control();
			_buildModeUI.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
			_buildModeUI.MouseFilter = Control.MouseFilterEnum.Ignore;
			_buildModeUI.Visible = false;

			var panel = new Panel();
			panel.Position = new Vector2(20, 20);
			panel.Size = new Vector2(350, 120);
			
			var vbox = new VBoxContainer();
			vbox.Position = new Vector2(10, 10);
			
			_buildModeLabel = new Label();
			_buildModeLabel.Text = "BUILD MODE";
			
			_costLabel = new Label();
			_costLabel.Text = "Cost: R0";
			
			var instructionLabel = new Label();
			instructionLabel.Text = "Q/E: Rotate | Left Click: Place | Right Click/ESC: Cancel\n1-9: Change Building Type";
			instructionLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
			
			vbox.AddChild(_buildModeLabel);
			vbox.AddChild(_costLabel);
			vbox.AddChild(instructionLabel);
			panel.AddChild(vbox);
			_buildModeUI.AddChild(panel);

			// Add to scene with CanvasLayer for proper rendering order
			var canvasLayer = new CanvasLayer();
			canvasLayer.Layer = 100; // Ensure it renders on top
			AddChild(canvasLayer);
			canvasLayer.AddChild(_buildModeUI);
		}

		public override void _UnhandledInput(InputEvent @event)
		{
			// Toggle build mode with 'B' key
			if (@event.IsActionPressed("toggle_build_mode"))
			{
				ToggleBuildMode();
				GetViewport().SetInputAsHandled();
				return;
			}

			if (!_isInBuildMode)
				return;

			// Handle build mode inputs
			if (@event.IsActionPressed("ui_cancel") || 
				(@event is InputEventMouseButton mb && mb.ButtonIndex == MouseButton.Right && mb.Pressed))
			{
				ExitBuildMode();
				GetViewport().SetInputAsHandled();
				return;
			}

			if (@event.IsActionPressed("rotate_left"))
			{
				_currentRotation -= Mathf.DegToRad(RotationStep);
				_currentRotation = Mathf.Wrap(_currentRotation, 0.0f, Mathf.Tau);
				GetViewport().SetInputAsHandled();
			}

			if (@event.IsActionPressed("rotate_right"))
			{
				_currentRotation += Mathf.DegToRad(RotationStep);
				_currentRotation = Mathf.Wrap(_currentRotation, 0.0f, Mathf.Tau);
				GetViewport().SetInputAsHandled();
			}

			// Place building with left mouse button
			if (@event is InputEventMouseButton mouseButton && 
				mouseButton.ButtonIndex == MouseButton.Left && mouseButton.Pressed)
			{
				if (_isPlacementValid)
				{
					PlaceBuilding();
				}
				GetViewport().SetInputAsHandled();
			}

			// Handle building type selection with number keys
			if (@event is InputEventKey keyEvent && keyEvent.Pressed)
			{
				if (keyEvent.Keycode >= Key.Key1 && keyEvent.Keycode <= Key.Key9)
				{
					int typeIndex = (int)(keyEvent.Keycode - Key.Key1);
					var buildingTypes = Enum.GetValues<BuildingType>();
					
					if (typeIndex < buildingTypes.Length)
					{
						SetBuildingType(buildingTypes[typeIndex]);
						GetViewport().SetInputAsHandled();
					}
				}
			}
		}

		public override void _Process(double delta)
		{
			if (!_isInBuildMode || PlayerCamera == null)
				return;

			UpdateGhostPreview();
		}

		/// <summary>
		/// Toggles build mode on/off
		/// </summary>
		private void ToggleBuildMode()
		{
			if (_isInBuildMode)
				ExitBuildMode();
			else
				EnterBuildMode();
		}

		/// <summary>
		/// Enters building placement mode
		/// </summary>
		private void EnterBuildMode()
		{
			_isInBuildMode = true;
			_currentRotation = 0.0f;
			
			CreateGhostPreview();
			
			_buildModeUI.Visible = true;
			UpdateUI();
			
			// Optionally change mouse mode for better visibility
			Input.MouseMode = Input.MouseModeEnum.Visible;
			
			GD.Print($"[BuildingPlacer] Entered build mode - Placing {_currentBuildingType}");
		}

		/// <summary>
		/// Exits building placement mode
		/// </summary>
		private void ExitBuildMode()
		{
			_isInBuildMode = false;
			
			DestroyGhostPreview();
			_buildModeUI.Visible = false;
			
			Input.MouseMode = Input.MouseModeEnum.Captured;
			
			GD.Print("[BuildingPlacer] Exited build mode");
		}

		/// <summary>
		/// Updates ghost preview position and validity
		/// </summary>
		private void UpdateGhostPreview()
		{
			if (_ghostPreview == null)
				return;

			Vector3 placementPosition = GetPlacementPositionFromMouse();

			if (placementPosition != Vector3.Zero)
			{
				_lastValidPosition = placementPosition;
				_ghostPreview.GlobalPosition = placementPosition;
				_ghostPreview.Rotation = new Vector3(0, _currentRotation, 0);

				// Validate placement using BuildingSystem
				_isPlacementValid = BuildingSystem.Instance?.CanPlaceBuilding(
					_currentBuildingType, placementPosition, _currentRotation) ?? false;

				// Also check if player can afford it
				if (_isPlacementValid && BuildingSystem.Instance != null)
				{
					int cost = BuildingSystem.BuildingCosts[_currentBuildingType];
					_isPlacementValid = EconomySystem.Instance?.CanAfford(cost) ?? false;
				}

				// Update ghost material based on validity
				if (_ghostMesh != null)
				{
					_ghostMesh.MaterialOverride = _isPlacementValid ? _validMaterial : _invalidMaterial;
				}

				_ghostPreview.Visible = true;
			}
			else
			{
				_ghostPreview.Visible = false;
				_isPlacementValid = false;
			}

			UpdateUI();
		}

		/// <summary>
		/// Performs raycast from mouse position to find placement location
		/// </summary>
		private Vector3 GetPlacementPositionFromMouse()
		{
			if (PlayerCamera == null)
				return Vector3.Zero;

			Vector2 mousePos = GetViewport().GetMousePosition();
			
			Vector3 rayOrigin = PlayerCamera.ProjectRayOrigin(mousePos);
			Vector3 rayDirection = PlayerCamera.ProjectRayNormal(mousePos);
			Vector3 rayEnd = rayOrigin + rayDirection * MaxPlacementDistance;

			// Raycast against terrain/world
			PhysicsDirectSpaceState3D spaceState = GetWorld3D().DirectSpaceState;
			var query = PhysicsRayQueryParameters3D.Create(rayOrigin, rayEnd);
			query.CollideWithAreas = false;
			query.CollideWithBodies = true;

			var result = spaceState.IntersectRay(query);

			if (result.Count > 0)
			{
				Vector3 hitPosition = (Vector3)result["position"];
				
				// Snap to terrain height for consistency
				float terrainHeight = TerrainGenerator.GetTerrainHeight(hitPosition.X, hitPosition.Z);
				hitPosition.Y = terrainHeight;

				return hitPosition;
			}

			return Vector3.Zero;
		}

		/// <summary>
		/// Places the building at the current ghost position
		/// </summary>
		private void PlaceBuilding()
		{
			if (BuildingSystem.Instance == null)
			{
				GD.PrintErr("[BuildingPlacer] BuildingSystem not available");
				return;
			}

			BuildingData placedBuilding = BuildingSystem.Instance.PlaceBuilding(
				_currentBuildingType,
				_lastValidPosition,
				_currentRotation
			);

			if (placedBuilding != null)
			{
				GD.Print($"[BuildingPlacer] Successfully placed {_currentBuildingType}");
				// TODO: Play placement sound effect
			}
			else
			{
				GD.PrintErr("[BuildingPlacer] Failed to place building");
				// TODO: Play error sound effect
			}
		}

		/// <summary>
		/// Creates the ghost preview mesh based on current building type
		/// </summary>
		private void CreateGhostPreview()
		{
			if (_ghostPreview != null)
				return;

			_ghostPreview = new Node3D();
			AddChild(_ghostPreview);

			UpdateGhostMesh();
		}

		/// <summary>
		/// Updates the ghost mesh when building type changes
		/// </summary>
		private void UpdateGhostMesh()
		{
			if (_ghostPreview == null)
				return;

			// Remove existing mesh
			if (_ghostMesh != null)
			{
				_ghostMesh.QueueFree();
			}

			// Create new mesh instance
			_ghostMesh = new MeshInstance3D();
			_ghostPreview.AddChild(_ghostMesh);

			// Set mesh based on building type
			Vector2 dimensions = BuildingSystem.BuildingDimensions[_currentBuildingType];
			_ghostMesh.Mesh = CreateMeshForBuildingType(_currentBuildingType, dimensions);
			_ghostMesh.MaterialOverride = _validMaterial;
		}

		/// <summary>
		/// Creates appropriate mesh for each building type
		/// </summary>
		private Mesh CreateMeshForBuildingType(BuildingType type, Vector2 dimensions)
		{
			return type switch
			{
				BuildingType.Wall => new BoxMesh { Size = new Vector3(dimensions.X, 3.0f, dimensions.Y) },
				BuildingType.Roof => new PrismMesh { Size = new Vector3(dimensions.X, 2.0f, dimensions.Y) },
				BuildingType.Floor or BuildingType.Stoep => new BoxMesh { Size = new Vector3(dimensions.X, 0.2f, dimensions.Y) },
				BuildingType.Door => new BoxMesh { Size = new Vector3(dimensions.X, 2.5f, dimensions.Y) },
				BuildingType.Window => new BoxMesh { Size = new Vector3(dimensions.X, 1.5f, dimensions.Y) },
				BuildingType.WaterTrough or BuildingType.FeedingStation => new BoxMesh { Size = new Vector3(dimensions.X, 0.6f, dimensions.Y) },
				BuildingType.BraaiArea or BuildingType.Boma => new CylinderMesh 
				{ 
					TopRadius = dimensions.X / 2.0f, 
					BottomRadius = dimensions.X / 2.0f, 
					Height = 0.8f 
				},
				BuildingType.StaffQuarters or BuildingType.Hide => new BoxMesh { Size = new Vector3(dimensions.X, 3.5f, dimensions.Y) },
				_ => new BoxMesh { Size = new Vector3(dimensions.X, 2.0f, dimensions.Y) }
			};
		}

		/// <summary>
		/// Destroys the ghost preview
		/// </summary>
		private void DestroyGhostPreview()
		{
			if (_ghostPreview != null)
			{
				_ghostPreview.QueueFree();
				_ghostPreview = null;
				_ghostMesh = null;
			}
		}

		/// <summary>
		/// Updates the UI display
		/// </summary>
		private void UpdateUI()
		{
			if (_buildModeLabel == null || _costLabel == null)
				return;

			_buildModeLabel.Text = $"BUILD MODE: {_currentBuildingType}";
			
			int cost = BuildingSystem.BuildingCosts[_currentBuildingType];
			float balance = EconomySystem.Instance?.GetBalance() ?? 0f;
			bool canAfford = balance >= cost;
			
			_costLabel.Text = $"Cost: R{cost} | Balance: R{balance}";
			_costLabel.Modulate = canAfford ? Colors.White : Colors.Red;
		}

		/// <summary>
		/// Sets the current building type programmatically
		/// </summary>
		public void SetBuildingType(BuildingType type)
		{
			_currentBuildingType = type;
			
			if (_isInBuildMode)
			{
				UpdateGhostMesh();
				UpdateUI();
			}
			
			GD.Print($"[BuildingPlacer] Changed to building type: {type}");
		}
	}
}
