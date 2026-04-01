## Boer.gd
## Savanna Sim — v0.1
## Third-person CharacterBody3D.
## Handles: on-foot movement, bakkie enter/exit, camera spring arm.
## Attach to: Boer.tscn (CharacterBody3D)

extends CharacterBody3D

# ── NODES ─────────────────────────────────────────────────────────────────────
@onready var spring_arm   : SpringArm3D  = $SpringArm3D
@onready var camera       : Camera3D     = $SpringArm3D/Camera3D
@onready var mesh         : Node3D = $Boer
@onready var anim         : AnimationPlayer = $AnimationPlayer

# ── SETTINGS — ON FOOT ────────────────────────────────────────────────────────
@export var walk_speed      : float = 4.0
@export var run_speed       : float = 8.0
@export var jump_velocity   : float = 4.5
@export var rotate_speed    : float = 10.0   # how fast the mesh turns to face direction

# ── SETTINGS — CAMERA ─────────────────────────────────────────────────────────
@export var cam_sensitivity : float = 0.003  # mouse look sensitivity
@export var cam_min_pitch   : float = -60.0  # degrees
@export var cam_max_pitch   : float = 20.0
@export var cam_distance    : float = 6.0    # spring arm length

# ── STATE ─────────────────────────────────────────────────────────────────────
enum State { FOOT, BAKKIE }
var state : State = State.FOOT

var gravity        : float = ProjectSettings.get_setting("physics/3d/default_gravity")
var _cam_yaw       : float = 0.0   # horizontal camera rotation (Y axis)
var _cam_pitch     : float = -20.0 # vertical camera rotation (X axis), start slightly above
var _current_bakkie : Node3D = null # reference to the bakkie node we are driving
var _streamer      : Node = null    # cached WorldChunkStreamer reference

func _ready() -> void:
	spring_arm.spring_length = cam_distance
	Input.mouse_mode = Input.MOUSE_MODE_CAPTURED

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
		print("[Player] Snapped to terrain height: ", h, " at (", global_position.x, ", ", global_position.z, ")")
	else:
		push_warning("[Player] WorldChunkStreamer not found — using scene Y position.")

# ── INPUT ─────────────────────────────────────────────────────────────────────
func _unhandled_input(event: InputEvent) -> void:
	# Mouse look
	if event is InputEventMouseMotion and Input.mouse_mode == Input.MOUSE_MODE_CAPTURED:
		_cam_yaw   -= event.relative.x * cam_sensitivity
		_cam_pitch  -= event.relative.y * cam_sensitivity
		_cam_pitch   = deg_to_rad(clamp(rad_to_deg(_cam_pitch), cam_min_pitch, cam_max_pitch))

	# Release / capture mouse
	if event.is_action_pressed("ui_cancel"):
		if Input.mouse_mode == Input.MOUSE_MODE_CAPTURED:
			Input.mouse_mode = Input.MOUSE_MODE_VISIBLE
		else:
			Input.mouse_mode = Input.MOUSE_MODE_CAPTURED

	# Enter / exit bakkie
	if event.is_action_pressed("interact"):
		match state:
			State.FOOT:   _try_enter_bakkie()
			State.BAKKIE: _exit_bakkie()

# ── PHYSICS ───────────────────────────────────────────────────────────────────
func _physics_process(delta: float) -> void:
	match state:
		State.FOOT:   _process_foot(delta)
		State.BAKKIE: _process_bakkie(delta)

	_update_camera()

# ── ON FOOT ───────────────────────────────────────────────────────────────────
func _process_foot(delta: float) -> void:
	# Gravity and Anti-Void Protection (raycast-based)
	if not is_on_floor():
		velocity.y -= gravity * delta

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

	# Jump
	if Input.is_action_just_pressed("jump") and is_on_floor():
		velocity.y = jump_velocity

	# WASD direction relative to camera facing
	var input_dir := Input.get_vector("move_forward", "move_back", "move_right", "move_left")
	var cam_basis  := Basis(Vector3.UP, _cam_yaw)
	var direction  := (cam_basis * Vector3(input_dir.x, 0, input_dir.y)).normalized()

	var speed := run_speed if Input.is_action_pressed("run") else walk_speed

	if direction != Vector3.ZERO:
		velocity.x = direction.x * speed
		velocity.z = direction.z * speed
		# The imported mesh has +X as its local forward instead of Godot's -Z,
		# so rotate the direction by -90° around Y before passing to looking_at().
		var target_basis := Basis.looking_at(direction, Vector3.UP)
		mesh.basis = mesh.basis.slerp(target_basis, rotate_speed * delta)
	else:
		velocity.x = move_toward(velocity.x, 0, speed)
		velocity.z = move_toward(velocity.z, 0, speed)

	move_and_slide()

# ── BAKKIE ────────────────────────────────────────────────────────────────────
func _process_bakkie(_delta: float) -> void:
	# Driving is handled by the Bakkie node itself.
	# Player just sits inside and the camera follows the bakkie's position.
	# We keep Player's global_position glued to the bakkie seat.
	if _current_bakkie:
		global_position = _current_bakkie.get_node("SeatPosition").global_position

func _try_enter_bakkie() -> void:
	# Look for a bakkie in range using an Area3D named InteractArea on the player
	var area := get_node_or_null("InteractArea") as Area3D
	if area == null:
		return
	for body in area.get_overlapping_bodies():
		if body.is_in_group("bakkie"):
			_current_bakkie = body
			state = State.BAKKIE
			mesh.visible = false                    # hide player mesh while seated
			$CollisionShape3D.disabled = true       # ADD THIS
			body.call("set_driver", self)           # tell bakkie it has a driver
			Input.mouse_mode = Input.MOUSE_MODE_CAPTURED
			break

func _exit_bakkie() -> void:
	if _current_bakkie:
		_current_bakkie.call("clear_driver")
		# Place player just beside the bakkie door
		global_position = _current_bakkie.global_position + \
			_current_bakkie.global_transform.basis.x * 2.0
		_current_bakkie = null
	$CollisionShape3D.disabled = false  # ADD THIS
	mesh.visible = true
	state = State.FOOT

# ── CAMERA ────────────────────────────────────────────────────────────────────
func _update_camera() -> void:
	# Rotate the spring arm to match mouse look
	spring_arm.rotation.y     = _cam_yaw
	spring_arm.rotation.x     = _cam_pitch
