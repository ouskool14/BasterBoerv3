## Terrain.gd
## Savanna Sim — v0.2
## Procedural low-poly terrain using FastNoiseLite.
## Attach to: StaticBody3D
## Children needed: MeshInstance3D

extends StaticBody3D

# ── NODES ─────────────────────────────────────────────────────────────────────
@onready var mesh_instance : MeshInstance3D = $MeshInstance3D

# ── SETTINGS ──────────────────────────────────────────────────────────────────
@export var width           : int   = 200
@export var depth           : int   = 200
@export var subdivisions    : int   = 80
@export var height_scale    : float = 12.0
@export var noise_frequency : float = 0.008

# ── COLOURS ───────────────────────────────────────────────────────────────────
@export var col_low  : Color = Color(0.76, 0.60, 0.42)
@export var col_mid  : Color = Color(0.55, 0.52, 0.28)
@export var col_high : Color = Color(0.42, 0.38, 0.22)

# ── READY ─────────────────────────────────────────────────────────────────────
func _ready() -> void:
	call_deferred("_generate")

# ── GENERATE ──────────────────────────────────────────────────────────────────
func _generate() -> void:
	# Detail noise — actual hill shapes
	var noise := FastNoiseLite.new()
	noise.noise_type = FastNoiseLite.TYPE_SIMPLEX_SMOOTH
	noise.frequency  = noise_frequency
	noise.seed       = randi()

	# Mask noise — controls where hills are allowed
	var mask := FastNoiseLite.new()
	mask.noise_type = FastNoiseLite.TYPE_SIMPLEX_SMOOTH
	mask.frequency  = 0.003	# very low frequency = large flat zones
	mask.seed       = randi()

	var step_x : float = float(width) / subdivisions
	var step_z : float = float(depth) / subdivisions

	# Build height grid first
	var heights := []
	for row in range(subdivisions + 1):
		var row_arr := []
		for col in range(subdivisions + 1):
			var x : float = col * step_x - width * 0.5
			var z : float = row * step_z - depth * 0.5
			var m : float = clamp(mask.get_noise_2d(x, z) * 2.0, 0.0, 1.0)
			m = pow(m, 3.0)  # pushes 80% of values toward zero
			var h : float = noise.get_noise_2d(x, z) * height_scale * m
			row_arr.append(h)
		heights.append(row_arr)

	# Build mesh arrays — flat shaded (each tri has its own 3 verts)
	var verts   := PackedVector3Array()
	var normals := PackedVector3Array()
	var colors  := PackedColorArray()
	var indices := PackedInt32Array()

	for row in range(subdivisions):
		for col in range(subdivisions):
			var x0 : float = col       * step_x - width * 0.5
			var x1 : float = (col + 1) * step_x - width * 0.5
			var z0 : float = row       * step_z - depth * 0.5
			var z1 : float = (row + 1) * step_z - depth * 0.5

			var h00 : float = heights[row][col]
			var h10 : float = heights[row][col + 1]
			var h01 : float = heights[row + 1][col]
			var h11 : float = heights[row + 1][col + 1]

			var v0 := Vector3(x0, h00, z0)
			var v1 := Vector3(x1, h10, z0)
			var v2 := Vector3(x0, h01, z1)
			var v3 := Vector3(x1, h10, z0)
			var v4 := Vector3(x1, h11, z1)
			var v5 := Vector3(x0, h01, z1)

			var n1 := (v1 - v0).cross(v2 - v0).normalized()
			var n2 := (v4 - v3).cross(v5 - v3).normalized()

			var c1 := _height_color((h00 + h01 + h10) / 3.0)
			var c2 := _height_color((h10 + h01 + h11) / 3.0)

			var base : int = verts.size()
			verts.append_array([v0, v1, v2, v3, v4, v5])
			normals.append_array([n1, n1, n1, n2, n2, n2])
			colors.append_array([c1, c1, c1, c2, c2, c2])
			indices.append_array([base, base+1, base+2, base+3, base+4, base+5])

	# Build ArrayMesh
	var arrays := []
	arrays.resize(Mesh.ARRAY_MAX)
	arrays[Mesh.ARRAY_VERTEX] = verts
	arrays[Mesh.ARRAY_NORMAL] = normals
	arrays[Mesh.ARRAY_COLOR]  = colors
	arrays[Mesh.ARRAY_INDEX]  = indices

	var arr_mesh := ArrayMesh.new()
	arr_mesh.add_surface_from_arrays(Mesh.PRIMITIVE_TRIANGLES, arrays)

	# Material
	var mat := StandardMaterial3D.new()
	mat.vertex_color_use_as_albedo = true
	mat.shading_mode = BaseMaterial3D.SHADING_MODE_PER_PIXEL
	arr_mesh.surface_set_material(0, mat)

	mesh_instance.mesh = arr_mesh

	# Build HeightMapShape3D — much more reliable for terrain
	var hmap := HeightMapShape3D.new()
	hmap.map_width  = subdivisions + 1
	hmap.map_depth  = subdivisions + 1

	var map_data := PackedFloat32Array()
	for row in range(subdivisions + 1):
		for col in range(subdivisions + 1):
			map_data.append(heights[row][col])

	hmap.map_data = map_data

	var col_shape := CollisionShape3D.new()
	col_shape.shape = hmap

	# Scale to match terrain size
	col_shape.scale = Vector3(
		float(width)  / subdivisions,
		1.0,
		float(depth)  / subdivisions
	)

	add_child(col_shape)
	print("Collision set: ", col_shape.shape)

# ── COLOUR BY HEIGHT ──────────────────────────────────────────────────────────
func _height_color(h: float) -> Color:
	var t : float = clamp(h / height_scale, -1.0, 1.0) * 0.5 + 0.5
	if t < 0.4:
		return col_low.lerp(col_mid, t / 0.4)
	else:
		return col_mid.lerp(col_high, (t - 0.4) / 0.6)
