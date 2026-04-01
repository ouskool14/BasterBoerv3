extends CanvasLayer

@onready var _label: Label = $StatsLabel

func _ready() -> void:
	visible = false

func _unhandled_input(event: InputEvent) -> void:
	if event is InputEventKey and event.pressed and not event.echo:
		if event.keycode == KEY_F3:
			visible = not visible

func _process(_delta: float) -> void:
	if not visible:
		return

	var fps: int = Engine.get_frames_per_second()
	var draw_calls: int = RenderingServer.get_rendering_info(
		RenderingServer.RENDERING_INFO_TOTAL_DRAW_CALLS_IN_FRAME
	)

	var active_chunks: String = "N/A"
	var streamer = get_node_or_null("/root/WorldChunkStreamer")
	if streamer:
		var count = streamer.get("ActiveChunkCount")
		if count != null:
			active_chunks = str(count)

	var flora_text: String = "N/A"
	var flora = get_node_or_null("/root/FloraSystem")
	if flora:
		var total = flora.get("TotalFloraCount")
		if total != null:
			flora_text = str(total)

	var mem_bytes: int = OS.get_static_memory_usage()
	var mem_mb: float = mem_bytes / (1024.0 * 1024.0)

	var lines: PackedStringArray = [
		"FPS: %d" % fps,
		"Draw calls: %d" % draw_calls,
		"Active chunks: %s" % active_chunks,
		"Flora count: %s" % flora_text,
		"Memory: %.1f MB" % mem_mb,
	]

	_label.text = "\n".join(lines)
