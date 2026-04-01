## Bakkie.gd
## Savanna Sim — v0.1
## Vehicle node. Handles driving when a player is seated.
## Attach to: Bakkie.tscn (CharacterBody3D or RigidBody3D — CharacterBody recommended for simplicity)

extends CharacterBody3D

# ── SETTINGS ──────────────────────────────────────────────────────────────────
@export var drive_speed      : float = 14.0
@export var reverse_speed    : float = 5.0
@export var turn_speed       : float = 2.0
@export var friction         : float = 8.0

# ── STATE ─────────────────────────────────────────────────────────────────────
var _driver : Node = null
var _has_driver : bool = false
var _streamer : Node = null    # cached WorldChunkStreamer reference

var gravity : float = ProjectSettings.get_setting("physics/3d/default_gravity")

func _ready() -> void:
	# Cache streamer reference via Godot autoload path
	# (GDScript can't access C# static properties like .Instance)
	_streamer = get_node_or_null("/root/WorldChunkStreamer")

	# If terrain isn't loaded yet, wait for the signal from WorldChunkStreamer
	# that confirms the center chunk's collision is in the scene tree.
	if _streamer and not _streamer.IsInitialLoadComplete:
		await _streamer.InitialTerrainReady

	_snap_to_terrain()

func _snap_to_terrain() -> void:
	if _streamer and _streamer.has_method("GetTerrainHeightAt"):
		var h : float = _streamer.GetTerrainHeightAt(global_position.x, global_position.z)
		global_position.y = h + 1.0
		velocity.y = 0.0
		move_and_slide()
		print("[Bakkie] Snapped to terrain height: ", h, " at (", global_position.x, ", ", global_position.z, ")")
	else:
		push_warning("[Bakkie] WorldChunkStreamer not found — using scene Y position.")

# ── API (called by Player) ────────────────────────────────────────────────────
func set_driver(player: Node) -> void:
	print("set_driver called!")
	print(get_stack())
	_driver     = player
	_has_driver = true

func clear_driver() -> void:
	_driver     = null
	_has_driver = false

# ── PHYSICS ───────────────────────────────────────────────────────────────────
func _physics_process(delta: float) -> void:
	print("has_driver: ", _has_driver)
	if not is_on_floor():
		velocity.y -= gravity * delta

		# Anti-Void Protection (raycast-based)
		# Raycast down to find the actual collision surface, not a mathematical height.
		# This avoids the bounce loop caused by height/collision mismatch.
		var space_state := get_world_3d().direct_space_state
		var ray_origin := global_position + Vector3.UP * 2.0  # start slightly above
		var ray_end := global_position + Vector3.DOWN * 50.0  # cast 50m below
		var query := PhysicsRayQueryParameters3D.create(ray_origin, ray_end)
		query.collision_mask = 1  # default physics layer (terrain)
		var result := space_state.intersect_ray(query)

		if result:
			var ground_y : float = result.position.y
			# Only teleport up if we are 3+ metres below the actual ground surface
			if global_position.y < ground_y - 3.0:
				global_position.y = ground_y + 1.0
				velocity.y = 0.0
		else:
			# No ground found within 50m — fallback to streamer math height
			if _streamer and _streamer.has_method("GetTerrainHeightAt"):
				var h : float = _streamer.GetTerrainHeightAt(global_position.x, global_position.z)
				if global_position.y < h - 5.0:
					global_position.y = h + 1.0
					velocity.y = 0.0

	if not _has_driver:
		# No driver — coast to a stop
		velocity.x = move_toward(velocity.x, 0, friction * delta)
		velocity.z = move_toward(velocity.z, 0, friction * delta)
		move_and_slide()
		return

	# Throttle
	var throttle := Input.get_axis("move_back", "move_forward")  # W/S
	var steer    := Input.get_axis("move_right", "move_left")    # A/D (inverted for natural feel)

	# Turn (only when moving)
	if abs(velocity.length()) > 0.5:
		rotation.y += steer * turn_speed * delta * sign(throttle if throttle != 0.0 else 1.0)

	# Accelerate forward / reverse
	var forward := -global_transform.basis.z
	if throttle > 0:
		velocity.x = forward.x * drive_speed * throttle
		velocity.z = forward.z * drive_speed * throttle
	elif throttle < 0:
		velocity.x = forward.x * reverse_speed * throttle
		velocity.z = forward.z * reverse_speed * throttle
	else:
		velocity.x = move_toward(velocity.x, 0, friction * delta)
		velocity.z = move_toward(velocity.z, 0, friction * delta)

	move_and_slide()
