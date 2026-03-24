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

# ── READY ─────────────────────────────────────────────────────────────────────
func _ready() -> void:
	spring_arm.spring_length = cam_distance
	Input.mouse_mode = Input.MOUSE_MODE_CAPTURED

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
	# Gravity
	if not is_on_floor():
		velocity.y -= gravity * delta

	# Jump
	if Input.is_action_just_pressed("jump") and is_on_floor():
		velocity.y = jump_velocity

	# WASD direction relative to camera facing
	var input_dir := Input.get_vector("move_left", "move_right", "move_forward", "move_back")
	var cam_basis  := Basis(Vector3.UP, _cam_yaw)
	var direction  := (cam_basis * Vector3(input_dir.x, 0, input_dir.y)).normalized()

	var speed := run_speed if Input.is_action_pressed("run") else walk_speed

	if direction != Vector3.ZERO:
		velocity.x = direction.x * speed
		velocity.z = direction.z * speed
		# Rotate mesh to face movement direction
		var target_basis := Basis.looking_at(direction, Vector3.UP)
		mesh.basis = mesh.basis.slerp(target_basis, rotate_speed * delta)
	else:
		velocity.x = move_toward(velocity.x, 0, speed)
		velocity.z = move_toward(velocity.z, 0, speed)

	move_and_slide()

# ── BAKKIE ────────────────────────────────────────────────────────────────────
func _process_bakkie(delta: float) -> void:
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
