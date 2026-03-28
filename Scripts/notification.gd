extends Control

signal dismissed

@onready var message_label: Label = $MessageLabel
@onready var dismiss_button: Button = $DismissButton

var auto_dismiss_timer: Timer

func _ready():
	dismiss_button.pressed.connect(_on_dismiss)
	
	# Auto-dismiss after 5 seconds
	auto_dismiss_timer = Timer.new()
	auto_dismiss_timer.wait_time = 5.0
	auto_dismiss_timer.one_shot = true
	auto_dismiss_timer.timeout.connect(_on_dismiss)
	add_child(auto_dismiss_timer)
	auto_dismiss_timer.start()

func set_message(text: String):
	await ready
	message_label.text = text

func _on_dismiss():
	dismissed.emit()
	queue_free()
