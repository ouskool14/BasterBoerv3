extends PanelContainer

signal dismissed

func _on_timer_timeout():
	hide()
	dismissed.emit()
	queue_free() # Remove the notification after it's dismissed

func set_message(message: String):
	$MarginContainer/MessageLabel.text = message

func show_notification():
	show()
	$Timer.start()
