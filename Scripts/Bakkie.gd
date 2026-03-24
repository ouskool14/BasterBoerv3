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

var gravity : float = ProjectSettings.get_setting("physics/3d/default_gravity")

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
		rotation.y += steer * turn_speed * delta * sign(throttle if throttle != 0 else 1)

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
