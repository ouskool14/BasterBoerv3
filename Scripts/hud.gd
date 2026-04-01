extends CanvasLayer

@onready var date_label = $"DateDisplay/DateLabel"
@onready var time_label = $"DateDisplay/TimeLabel"
@onready var season_label = $"DateDisplay/SeasonLabel"
@onready var financial_summary = $"FinancialSummary"
@onready var balance_label = $"FinancialSummary/BalanceLabel"
@onready var income_expense_arrow = $"FinancialSummary/IncomeExpenseArrow"
@onready var interact_prompt = $"InteractPrompt"
@onready var prompt_label = $"InteractPrompt/PromptLabel"
@onready var notification_system_container = $"NotificationSystem"
@onready var vehicle_hud = $"VehicleHUD"
@onready var speed_label = $"VehicleHUD/SpeedLabel"
@onready var fuel_gauge = $"VehicleHUD/FuelGauge"

var _game_state = null
var _time_system = null
var _economy_system = null
var _player = null

var notification_scene = preload("res://Scenes/notification.tscn")
var active_notifications = []
const MAX_NOTIFICATIONS = 3

func _ready():
	# Get references through Bootstrap (the central system registry)
	var bootstrap = get_node_or_null("/root/Bootstrap")
	if bootstrap:
		_game_state = bootstrap.get("Game")
		_time_system = bootstrap.get("Time")
		_economy_system = bootstrap.get("Economy")
	
	# Fallback: try direct paths for backwards compatibility
	if not _game_state:
		_game_state = get_node_or_null("/root/GameState")
	if not _time_system:
		_time_system = get_node_or_null("/root/TimeSystem")
	if not _economy_system:
		_economy_system = get_node_or_null("/root/EconomySystem")
	
	_player = get_tree().root.find_child("Boer", true, false)

	# Connect signals from TimeSystem
	if _time_system:
		if _time_system.has_signal("OnDayPassed"):
			_time_system.OnDayPassed.connect(update_date_time)
		if _time_system.has_signal("OnMonthPassed"):
			_time_system.OnMonthPassed.connect(update_date_time)
		if _time_system.has_signal("OnSeasonChanged"):
			_time_system.OnSeasonChanged.connect(update_date_time)
		if _time_system.has_signal("OnYearPassed"):
			_time_system.OnYearPassed.connect(update_date_time)
		if _time_system.has_signal("OnSeasonalEvent"):
			_time_system.OnSeasonalEvent.connect(func(event, _date): add_notification("Seasonal Event: " + str(event)))

	# Connect signals from EconomySystem
	if _economy_system:
		if _economy_system.has_signal("OnCashBalanceChanged"):
			_economy_system.OnCashBalanceChanged.connect(update_financial_summary)
		if _economy_system.has_signal("OnMonthlyStatementReady"):
			_economy_system.OnMonthlyStatementReady.connect(func(_date): add_notification("Monthly Financial Statement Ready"))

	# Initial HUD updates
	update_date_time()
	update_financial_summary()
	
	# Set up financial summary fade out timer
	var financial_timer = Timer.new()
	financial_timer.wait_time = 3.0
	financial_timer.one_shot = true
	financial_timer.timeout.connect(func(): financial_summary.hide())
	add_child(financial_timer)
	financial_summary.set_meta("fade_timer", financial_timer)

func _process(_delta):
	update_interact_prompt()
	update_vehicle_hud()

func update_date_time(_arg1 = null, _arg2 = null):
	if _time_system:
		# Date
		if _time_system.has_method("get") and _time_system.get("CurrentDate") != null:
			date_label.text = str(_time_system.CurrentDate)
		
		# Time — use GetTimeString() since CurrentHour/CurrentMinute don't exist
		if _time_system.has_method("GetTimeString"):
			time_label.text = _time_system.GetTimeString()
		
		# Season
		if _time_system.get("CurrentSeason") != null:
			season_label.text = str(_time_system.CurrentSeason)

func update_financial_summary(_new_balance = null):
	if _game_state and _economy_system:
		var balance = _game_state.get("CashBalance")
		if balance != null:
			# South African Rand format: R 1,234.56
			balance_label.text = "R " + format_currency(balance)
		
		var net_income = _economy_system.get("MonthlyNetIncome")
		if net_income != null:
			if net_income > 0:
				income_expense_arrow.text = "↑"
				income_expense_arrow.modulate = Color.GREEN
			elif net_income < 0:
				income_expense_arrow.text = "↓"
				income_expense_arrow.modulate = Color.RED
			else:
				income_expense_arrow.text = ""
			
		financial_summary.show()
		var financial_timer = financial_summary.get_meta("fade_timer")
		if financial_timer:
			financial_timer.start()

func format_currency(value: float) -> String:
	var formatted = "%d" % int(value)
	var result = ""
	var count = 0
	for i in range(formatted.length() - 1, -1, -1):
		if count == 3:
			result = "," + result
			count = 0
		result = formatted[i] + result
		count += 1
	# Add decimals
	var decimals = fmod(abs(value), 1.0) * 100
	return result + ".%02d" % int(decimals)

func update_interact_prompt():
	if _player and _player.has_method("get_interactable_object"):
		var interactable = _player.get_interactable_object()
		if interactable:
			interact_prompt.show()
			prompt_label.text = "[E] Interact"
		else:
			interact_prompt.hide()
	else:
		interact_prompt.hide()

func update_vehicle_hud():
	# Placeholder — enable when bakkie driving is implemented
	vehicle_hud.hide()

func add_notification(message: String):
	if not notification_scene:
		return
	var notification_instance = notification_scene.instantiate()
	notification_system_container.add_child(notification_instance)
	notification_instance.set_message(message)
	notification_instance.dismissed.connect(func(): remove_notification(notification_instance))

	active_notifications.append(notification_instance)

	# Position notifications (stacking from top to bottom)
	for i in range(active_notifications.size()):
		var notif = active_notifications[i]
		notif.position = Vector2(0, i * 70)

	# Remove oldest notification if exceeding max
	if active_notifications.size() > MAX_NOTIFICATIONS:
		var oldest = active_notifications[0]
		oldest.queue_free()

func remove_notification(notification_instance):
	if active_notifications.has(notification_instance):
		active_notifications.erase(notification_instance)
		# Reposition remaining notifications
		for i in range(active_notifications.size()):
			var notif = active_notifications[i]
			notif.position = Vector2(0, i * 70)
