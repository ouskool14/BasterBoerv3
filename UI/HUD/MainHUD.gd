extends CanvasLayer

## Minimal always-visible HUD showing date, weather, money, and alert badge.
## Connects to C# systems via Bootstrap for reactive updates.

@onready var date_label: Label = $DateLabel
@onready var season_icon: Label = $SeasonIcon
@onready var time_label: Label = $TimeLabel
@onready var weather_label: Label = $WeatherLabel
@onready var money_label: Label = $MoneyLabel
@onready var alert_badge: Button = $AlertBadge

var _time_system = null
var _game_state = null
var _alert_system = null

var MONTH_NAMES = ["", "Jan", "Feb", "Mar", "Apr", "May", "Jun",
	"Jul", "Aug", "Sep", "Oct", "Nov", "Dec"]

var SEASON_ICONS = {
	0: "☀",  # Summer
	1: "🍂",  # Autumn
	2: "❄",   # Winter
	3: "🌱"   # Spring
}

func _ready():
	# Get systems through Bootstrap
	var bootstrap = get_node_or_null("/root/Bootstrap")
	if bootstrap:
		_time_system = bootstrap.get("Time")
		_game_state = bootstrap.get("Game")
	
	# Connect signals
	if _time_system:
		if _time_system.has_signal("OnDayPassed"):
			_time_system.OnDayPassed.connect(_on_day_passed)
		if _time_system.has_signal("OnSeasonChanged"):
			_time_system.OnSeasonChanged.connect(_on_season_changed)
	
	if _game_state:
		if _game_state.has_signal("WeatherChanged"):
			_game_state.WeatherChanged.connect(_on_weather_changed)
	
	# Initial update
	_update_display()

func _on_day_passed(_date = null, _arg2 = null):
	_update_display()

func _on_season_changed(_season = null, _date = null):
	_update_display()

func _on_weather_changed(_new_weather = null, _old_weather = null):
	_update_display()

func _process(_delta: float):
	# Update time display every frame for smooth ticking
	if _time_system and time_label:
		var time_str = _time_system.GetTimeString()
		if time_str:
			time_label.text = time_str

func _update_display():
	if _time_system:
		# Date
		if _time_system.get("CurrentDate") != null:
			date_label.text = str(_time_system.CurrentDate)
		
		# Season icon
		if _time_system.get("CurrentSeason") != null:
			var season_idx = int(_time_system.CurrentSeason)
			season_icon.text = SEASON_ICONS.get(season_idx, "?")
	
	# Money
	if _game_state:
		var balance = _game_state.get("CashBalance")
		if balance != null:
			money_label.text = "R " + format_currency(balance)
			# Red when low
			if balance < 10000:
				money_label.modulate = Color.RED
			else:
				money_label.modulate = Color.WHITE
	
	# Weather
	if _game_state:
		var weather = _game_state.get("CurrentWeather")
		if weather != null:
			weather_label.text = _weather_to_string(int(weather))

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
	var decimals = fmod(abs(value), 1.0) * 100
	return result + ".%02d" % int(decimals)

func _weather_to_string(weather_int: int) -> String:
	match weather_int:
		0: return "☀ Clear"
		1: return "⛅ Cloudy"
		2: return "☁ Overcast"
		3: return "🌧 Rain"
		4: return "⛈ Storm"
		5: return "🔥 Drought"
		_: return "?"

func update_alert_count(count: int):
	if alert_badge:
		alert_badge.text = "[%d alerts]" % count
		alert_badge.visible = count > 0
