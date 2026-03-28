extends CanvasLayer

@onready var date_label = $"DateDisplay/DateLabel"
@onready var time_label = $"DateDisplay/TimeLabel"
@onready var season_label = $"DateDisplay/SeasonLabel"
@onready var financial_summary = $"FinancialSummary"
@onready var balance_label = $"FinancialSummary/BalanceLabel"
@onready var income_expense_arrow = $"FinancialSummary/IncomeExpenseArrow"
@onready var compass = $"Compass"
@onready var compass_bar = $"Compass/CompassBar"
@onready var interact_prompt = $"InteractPrompt"
@onready var prompt_label = $"InteractPrompt/PromptLabel"
@onready var notification_system_container = $"NotificationSystem"
@onready var vehicle_hud = $"VehicleHUD"
@onready var speed_label = $"VehicleHUD/SpeedLabel"
@onready var fuel_gauge = $"VehicleHUD/FuelGauge"

var GameState = null
var TimeSystem = null
var EconomySystem = null
var Player = null # Reference to the Player.gd script

var notification_scene = preload("res://hud/scenes/notification.tscn")
var active_notifications = []
const MAX_NOTIFICATIONS = 3

func _ready():
	# Get references to C# singletons
	GameState = get_node("/root/GameState")
	TimeSystem = get_node("/root/TimeSystem")
	EconomySystem = get_node("/root/EconomySystem")
	Player = get_node("/root/Player") # Assuming Player.gd is an Autoload or directly accessible

	# Connect signals from TimeSystem
	if TimeSystem:
		TimeSystem.OnDayPassed.connect(update_date_time)
		TimeSystem.OnMonthPassed.connect(update_date_time)
		TimeSystem.OnSeasonChanged.connect(update_date_time)
		TimeSystem.OnYearPassed.connect(update_date_time)
		TimeSystem.OnSeasonalEvent.connect(func(event_name, game_date): add_notification("Seasonal Event: " + event_name))

	# Connect signals from EconomySystem
	if EconomySystem:
		EconomySystem.OnCashBalanceChanged.connect(update_financial_summary)
		EconomySystem.OnMonthlyStatementReady.connect(func(): add_notification("Monthly Financial Statement Ready"))

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

func _process(delta):
	update_compass()
	update_interact_prompt()
	update_vehicle_hud()

func update_date_time():
	if TimeSystem:
		var game_date = TimeSystem.CurrentDate
		date_label.text = game_date.ToString("dd MMMM yyyy") # e.g., 15 March 2024
		# Assuming TimeSystem has properties for current hour and minute
		# Placeholder for now, replace with actual TimeSystem properties if available
		time_label.text = "%.02d:%.02d" % [TimeSystem.CurrentHour, TimeSystem.CurrentMinute] if TimeSystem.has_method("CurrentHour") else "14:30"
		season_label.text = TimeSystem.CurrentSeason.ToString() # Assuming CurrentSeason is an enum or string

func update_financial_summary():
	if GameState and EconomySystem:
		balance_label.text = "R " + "%.2f" % GameState.CashBalance # Format to 2 decimal places
		
		var net_income = EconomySystem.MonthlyNetIncome
		if net_income > 0:
			income_expense_arrow.text = "↑"
			income_expense_arrow.modulate = Color("green")
		elif net_income < 0:
			income_expense_arrow.text = "↓"
			income_expense_arrow.modulate = Color("red")
		else:
			income_expense_arrow.text = ""
			
		financial_summary.show()
		var financial_timer = financial_summary.get_meta("fade_timer")
		if financial_timer:
			financial_timer.start()

func update_compass():
	if Player:
		var camera_basis = Player.get_node("SpringArm3D/Camera3D").global_transform.basis
		var forward_vector = -camera_basis.z
		var angle = atan2(forward_vector.x, forward_vector.z)
		compass_bar.rotation = angle

func update_interact_prompt():
	if Player:
		# This is a simplified check. A more robust solution would involve signals from interactable objects.
		# For now, we'll just check if the player is in the bakkie, as that's a known interaction.
		if Player.state == Player.State.FOOT and Player.has_method("get_interactable_object") and Player.get_interactable_object():
			interact_prompt.show()
			# You might want to get the actual interact key from project settings or a player input map
			prompt_label.text = "[E] Interact"
		else:
			interact_prompt.hide()

func update_vehicle_hud():
	if Player and Player.state == Player.State.BAKKIE:
		vehicle_hud.show()
		# Assuming the bakkie has properties for speed and fuel
		# Placeholder for now, replace with actual Bakkie properties if available
		speed_label.text = "Speed: %d km/h" % Player.current_bakkie.speed if Player.current_bakkie.has_method("speed") else "Speed: 0 km/h"
		fuel_gauge.value = Player.current_bakkie.fuel_level if Player.current_bakkie.has_method("fuel_level") else 50 # Assuming fuel_level is 0-100
	else:
		vehicle_hud.hide()

func add_notification(message: String):
	var notification_instance = notification_scene.instantiate()
	notification_system_container.add_child(notification_instance)
	notification_instance.set_message(message)
	notification_instance.dismissed.connect(func(): remove_notification(notification_instance))

	active_notifications.append(notification_instance)

	# Position notifications (stacking from top to bottom)
	for i in range(active_notifications.size()):
		var notif = active_notifications[i]
		notif.position = Vector2(0, i * (notif.custom_minimum_size.y + 10)) # 10 pixels padding

	# Remove oldest notification if exceeding max
	if active_notifications.size() > MAX_NOTIFICATIONS:
		active_notifications[0].queue_free() # This will also trigger dismissed signal and remove from list

func remove_notification(notification_instance):
	if active_notifications.has(notification_instance):
		active_notifications.erase(notification_instance)
		# Reposition remaining notifications
		for i in range(active_notifications.size()):
			var notif = active_notifications[i]
			notif.position = Vector2(0, i * (notif.custom_minimum_size.y + 10))
