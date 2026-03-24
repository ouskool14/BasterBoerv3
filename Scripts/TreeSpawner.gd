## TreeSpawner.gd
## Savanna Sim — v0.2
## Spawns low-poly trees and shrubs using raycasts to find exact terrain height.
## Attach to: Node3D (TreeSpawner) in main scene.

extends Node3D

# ── SETTINGS ──────────────────────────────────────────────────────────────────
@export var tree_count    : int   = 300
@export var shrub_count   : int   = 500
@export var terrain_width : int   = 200
@export var terrain_depth : int   = 200
@export var max_slope     : float = 0.4    # 0 = flat, 1 = vertical
@export var min_height    : float = -4.0   # don't spawn below this

# ── COLOURS ───────────────────────────────────────────────────────────────────
var col_acacia_canopy : Color = Color(0.45, 0.48, 0.22)
var col_acacia_trunk  : Color = Color(0.38, 0.28, 0.18)
var col_thorn_canopy  : Color = Color(0.35, 0.40, 0.20)
var col_dead_trunk    : Color = Color(0.50, 0.42, 0.30)
var col_shrub         : Color = Color(0.40, 0.44, 0.18)

# ── READY ─────────────────────────────────────────────────────────────────────
func _ready() -> void:
	call_deferred("_spawn")

# ── SPAWN ─────────────────────────────────────────────────────────────────────
func _spawn() -> void:
	var rng := RandomNumberGenerator.new()
	rng.randomize()

	var space := get_world_3d().direct_space_state

	_spawn_batch(rng, space, tree_count, false)
	_spawn_batch(rng, space, shrub_count, true)

func _spawn_batch(rng: RandomNumberGenerator, space: PhysicsDirectSpaceState3D, count: int, is_shrub: bool) -> void:
	var spawned  := 0
	var attempts := 0

	while spawned < count and attempts < count * 10:
		attempts += 1

		var x := rng.randf_range(-terrain_width * 0.5, terrain_width * 0.5)
		var z := rng.randf_range(-terrain_depth * 0.5, terrain_depth * 0.5)

		# Raycast straight down to find exact terrain surface
		var ray := PhysicsRayQueryParameters3D.create(
			Vector3(x, 200.0, z),
			Vector3(x, -200.0, z)
		)
		var result := space.intersect_ray(ray)

		if result.is_empty():
			continue

		var h : float = result.position.y

		if h < min_height:
			continue

		# Slope from surface normal — skip steep ground
		var slope : float = 1.0 - result.normal.dot(Vector3.UP)
		if slope > max_slope:
			continue

		var node : Node3D

		if is_shrub:
			var scale := rng.randf_range(0.3, 0.8)
			node = _build_shrub(scale)
		else:
			var scale := rng.randf_range(0.8, 1.6)
			var roll  := rng.randf()
			var type  := "acacia" if roll < 0.6 else ("thorn" if roll < 0.85 else "dead")
			node = _build_tree(type, scale)

		node.position   = Vector3(x, h, z)
		node.rotation.y = rng.randf_range(0.0, TAU)
		add_child(node)
		spawned += 1

	print("%s spawned: %d" % ["Shrubs" if is_shrub else "Trees", spawned])

# ── BUILD TREE ────────────────────────────────────────────────────────────────
func _build_tree(type: String, scale: float) -> Node3D:
	var root := Node3D.new()

	match type:
		"acacia":
			var trunk := _make_cylinder(0.08 * scale, 0.12 * scale, 1.8 * scale, 5, col_acacia_trunk)
			trunk.position.y = 0.9 * scale
			root.add_child(trunk)

			var canopy := _make_cylinder(0.0, 1.6 * scale, 0.5 * scale, 6, col_acacia_canopy)
			canopy.position.y = 1.9 * scale
			root.add_child(canopy)

		"thorn":
			var trunk := _make_cylinder(0.06 * scale, 0.10 * scale, 1.2 * scale, 5, col_acacia_trunk)
			trunk.position.y = 0.6 * scale
			root.add_child(trunk)

			var canopy := _make_sphere(0.9 * scale, 0.8 * scale, 5, 3, col_thorn_canopy)
			canopy.position.y = 1.4 * scale
			root.add_child(canopy)

		"dead":
			var trunk := _make_cylinder(0.04 * scale, 0.10 * scale, 2.0 * scale, 4, col_dead_trunk)
			trunk.position.y = 1.0 * scale
			root.add_child(trunk)

			for i in range(2):
				var branch := _make_cylinder(0.02 * scale, 0.04 * scale, 0.8 * scale, 4, col_dead_trunk)
				branch.position.y = 1.6 * scale
				branch.rotation.z = 0.6 * (1 if i == 0 else -1)
				root.add_child(branch)

	return root

# ── BUILD SHRUB ───────────────────────────────────────────────────────────────
func _build_shrub(scale: float) -> Node3D:
	var root := Node3D.new()
	var mesh := _make_sphere(0.5 * scale, 0.4 * scale, 4, 2, col_shrub)
	mesh.position.y = 0.2 * scale
	root.add_child(mesh)
	return root

# ── MESH HELPERS ──────────────────────────────────────────────────────────────
func _make_cylinder(top: float, bottom: float, height: float, segments: int, color: Color) -> MeshInstance3D:
	var m := CylinderMesh.new()
	m.top_radius      = top
	m.bottom_radius   = bottom
	m.height          = height
	m.radial_segments = segments
	m.cap_top         = true
	m.cap_bottom      = true
	return _wrap_mesh(m, color)

func _make_sphere(radius: float, height: float, segments: int, rings: int, color: Color) -> MeshInstance3D:
	var m := SphereMesh.new()
	m.radius          = radius
	m.height          = height
	m.radial_segments = segments
	m.rings           = rings
	return _wrap_mesh(m, color)

func _wrap_mesh(mesh_data: Mesh, color: Color) -> MeshInstance3D:
	var mat := StandardMaterial3D.new()
	mat.albedo_color = color
	mat.shading_mode = BaseMaterial3D.SHADING_MODE_PER_PIXEL
	mesh_data.material = mat

	var mi := MeshInstance3D.new()
	mi.mesh = mesh_data
	return mi
