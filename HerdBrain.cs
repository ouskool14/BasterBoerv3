using System;
using Godot;
using BasterBoer.Core.Systems;
using BasterBoer.Core.Water;

namespace LandManagementSim.Simulation
{
	/// <summary>
	/// Species-specific configuration data for herd behavior.
	/// </summary>
	public struct SpeciesConfig
	{
		public float BaseAwarenessRadius;
		public float FlightDistance;
		public int MinHerdSize;
		public int MaxHerdSize;
		public float DrinkFrequencyHours;
		public float GrazeSpeedMPS;
		public float WalkSpeedMPS;
		public float RunSpeedMPS;
		public float RestIntervalHours;
		public float MaxDailyTravelKm;
	}

	/// <summary>
	/// Central AI controller for a herd of social animals.
	/// Makes all behavioral decisions that individual animals execute with variation.
	/// Implements LOD-aware simulation for performance scaling.
	/// </summary>
	public sealed class HerdBrain
	{
		private readonly SpeciesConfig _config;
		private readonly Random _rng;

		// State tracking
		private float _updateAccumulator;
		private float _stateTime;
		private float _timeSinceLastDrink;
		private float _timeSinceLastRest;
		private float _dailyTravelDistance;
		private Vector3 _lastDayPosition;
		private Vector3 _lastThreatPosition;
		private float _currentSpreadRadius;
		private float _reproductionTimer = 0f;
		private const float REPRODUCTION_INTERVAL = 60f; // seconds (tune later)

		// Water tracking
		private int _targetWaterSourceId = -1;

		/// <summary>
		/// Species of this herd.
		/// </summary>
		public Species Species { get; }

		/// <summary>
		/// Center position of herd in world space.
		/// </summary>
		public Vector3 CenterPosition { get; private set; }

		/// <summary>
		/// Current behavioral state of the entire herd.
		/// </summary>
		public HerdState CurrentState { get; private set; }

		/// <summary>
		/// Current LOD tier based on distance from player.
		/// </summary>
		public BehaviourLOD CurrentLOD { get; private set; }

		/// <summary>
		/// Thirst level from 0.0 (hydrated) to 1.0 (critical).
		/// </summary>
		public float Thirst { get; private set; }

		/// <summary>
		/// Hunger level from 0.0 (fed) to 1.0 (starving).
		/// </summary>
		public float Hunger { get; private set; }

		/// <summary>
		/// Fatigue level from 0.0 (rested) to 1.0 (exhausted).
		/// </summary>
		public float Fatigue { get; private set; }

		/// <summary>
		/// Fear level from 0.0 (calm) to 1.0 (panicked).
		/// </summary>
		public float FearLevel { get; private set; }

		/// <summary>
		/// Current awareness radius in meters.
		/// </summary>
		public float AwarenessRadius { get; private set; }

		/// <summary>
		/// All animals belonging to this herd.
		/// </summary>
		public AnimalStruct[] Animals { get; internal set; }

		/// <summary>
		/// Unique identifier for this herd.
		/// </summary>
		public int HerdId { get; internal set; }

		/// <summary>
		/// Current movement direction for the herd.
		/// </summary>
		public Vector3 MovementDirection { get; private set; }

		/// <summary>
		/// Target position the herd is moving toward.
		/// </summary>
		public Vector3 TargetPosition { get; private set; }

		/// <summary>
		/// Total simulation time in hours.
		/// </summary>
		public double SimulatedHours { get; private set; }

		public HerdBrain(Species species, SpeciesConfig config, Vector3 initialCenter, AnimalStruct[] animals, int seed)
		{
			Species = species;
			_config = config;
			CenterPosition = initialCenter;
			Animals = animals;
			_rng = new Random(seed);

			// Initialize state
			CurrentState = HerdState.Grazing;
			_currentSpreadRadius = GetSpreadRadiusForState(CurrentState);
			CurrentLOD = BehaviourLOD.Full;
			AwarenessRadius = config.BaseAwarenessRadius;
			
			// Initialize needs at reasonable starting values
			Thirst = 0.25f;
			Hunger = 0.25f;
			Fatigue = 0.1f;
			FearLevel = 0f;

			// Initialize tracking variables
			_updateAccumulator = 0f;
			_stateTime = 0f;
			_timeSinceLastDrink = 0f;
			_timeSinceLastRest = 0f;
			_dailyTravelDistance = 0f;
			_lastDayPosition = initialCenter;
			MovementDirection = Vector3.Zero;
			TargetPosition = initialCenter;
		}

		/// <summary>
		/// Main simulation tick called by AnimalSystem.
		/// Handles LOD-aware behavior updates.
		/// </summary>
		/// <param name="deltaTime">Time since last update in seconds</param>
		/// <param name="playerPosition">Current player position for LOD calculation</param>
		public void Tick(float deltaTime, Vector3 playerPosition)
		{
			// Update LOD based on distance to player
			float distanceSquared = CenterPosition.DistanceSquaredTo(playerPosition);
			BehaviourLOD newLOD = BehaviourLODHelper.GetLODFromDistanceSquared(distanceSquared);
			
			if (newLOD != CurrentLOD)
			{
				CurrentLOD = newLOD;
				_updateAccumulator = 0f;
			}

			// Handle different LOD tiers
			switch (CurrentLOD)
			{
				case BehaviourLOD.Full:
					UpdateBehavior(deltaTime);
					break;

				case BehaviourLOD.High:
				case BehaviourLOD.Medium:
					float updateInterval = BehaviourLODHelper.GetUpdateInterval(CurrentLOD);
					_updateAccumulator += deltaTime;
					
					if (_updateAccumulator >= updateInterval)
					{
						UpdateBehavior(_updateAccumulator);
						_updateAccumulator = 0f;
					}
					break;

				case BehaviourLOD.Background:
					// Only monthly ticks handled by TimeSystem
					break;
			}
		}

		/// <summary>
		/// Core behavior update logic handling needs, state machine, and movement.
		/// </summary>
		private void UpdateBehavior(float deltaTime)
		{
			float deltaHours = deltaTime / 3600f;
			SimulatedHours += deltaHours;
			_stateTime += deltaTime;

			_reproductionTimer += deltaTime;
			if (_reproductionTimer >= REPRODUCTION_INTERVAL)
			{
				_reproductionTimer = 0f;
				TryReproduce();
			}

			// Update basic needs
			UpdateNeeds(deltaHours);

			// Natural fear decay
			if (CurrentState != HerdState.Fleeing && CurrentState != HerdState.Alerting)
			{
				FearLevel = Math.Max(0f, FearLevel - deltaTime * 0.1f);
			}

			// Adjust awareness radius based on fear level
			float targetAwareness = _config.BaseAwarenessRadius * (1f + FearLevel);
			AwarenessRadius = Mathf.Lerp(AwarenessRadius, targetAwareness, deltaTime * 2f);

			// State machine evaluation and execution
			EvaluateStateTransitions();
			ExecuteCurrentState(deltaTime);

			// Update individual animal positions and animations
			if (CurrentLOD == BehaviourLOD.Full)
			{
				UpdateIndividualAnimals(deltaTime);
			}
		}

		/// <summary>
		/// Updates herd needs based on current activity and time passage.
		/// </summary>
		private void UpdateNeeds(float deltaHours)
		{
			float thirstRate = 1f / _config.DrinkFrequencyHours;
			float hungerRate = 1f / 8f; // Graze every ~8 hours
			float fatigueRate = 1f / 16f; // Full exhaustion over 16 active hours

			switch (CurrentState)
			{
				case HerdState.Grazing:
					Hunger = Math.Max(0f, Hunger - deltaHours * 0.1f);
					Thirst += thirstRate * deltaHours;
					Fatigue += fatigueRate * deltaHours * 0.5f;
					break;

				case HerdState.Moving:
					Hunger += hungerRate * deltaHours;
					Thirst += thirstRate * deltaHours * 1.2f;
					Fatigue += fatigueRate * deltaHours;
					break;

				case HerdState.Drinking:
					Thirst = Math.Max(0f, Thirst - deltaHours * 2f);
					Hunger += hungerRate * deltaHours * 0.5f;
					Fatigue += fatigueRate * deltaHours * 0.3f;
					break;

				case HerdState.Resting:
					Fatigue = Math.Max(0f, Fatigue - deltaHours * 0.5f);
					Hunger += hungerRate * deltaHours * 0.5f;
					Thirst += thirstRate * deltaHours * 0.8f;
					break;

				case HerdState.Fleeing:
					Hunger += hungerRate * deltaHours * 1.5f;
					Thirst += thirstRate * deltaHours * 1.8f;
					Fatigue += fatigueRate * deltaHours * 2f;
					break;

				case HerdState.Alerting:
					Hunger += hungerRate * deltaHours * 0.3f;
					Thirst += thirstRate * deltaHours * 0.5f;
					Fatigue += fatigueRate * deltaHours * 0.8f;
					break;
			}

			// Clamp all needs to valid range
			Thirst = Math.Clamp(Thirst, 0f, 1f);
			Hunger = Math.Clamp(Hunger, 0f, 1f);
			Fatigue = Math.Clamp(Fatigue, 0f, 1f);

			// Update timers
			_timeSinceLastDrink += deltaHours;
			_timeSinceLastRest += deltaHours;
		}

		/// <summary>
		/// Evaluates conditions for state transitions based on needs and stimuli.
		/// Priority: Fear > Critical Needs > Normal Behavior Cycle
		/// </summary>
		private void EvaluateStateTransitions()
		{
			// Fear states have highest priority
			if (FearLevel > 0.7f)
			{
				TransitionToState(HerdState.Fleeing);
				return;
			}

			if (FearLevel > 0.3f && CurrentState != HerdState.Alerting)
			{
				TransitionToState(HerdState.Alerting);
				return;
			}

			// Critical needs override normal behavior
			if (Thirst > 0.8f || _timeSinceLastDrink >= _config.DrinkFrequencyHours)
			{
				if (CurrentState != HerdState.Drinking)
				{
					TransitionToState(HerdState.Moving);
					SetWaterTarget();
				}
				return;
			}

			if (Fatigue > 0.75f && _timeSinceLastRest > _config.RestIntervalHours)
			{
				TransitionToState(HerdState.Resting);
				return;
			}

			// Normal behavior cycle based on current state
			switch (CurrentState)
			{
				case HerdState.Grazing:
					if (Hunger < 0.2f && _stateTime > 300f) // Satisfied after 5 minutes
					{
						if (Thirst > 0.5f)
						{
							TransitionToState(HerdState.Moving);
							SetWaterTarget();
						}
						else if (_rng.NextDouble() < 0.2) // Random movement
						{
							TransitionToState(HerdState.Moving);
							SetRandomTarget();
						}
					}
					break;

				case HerdState.Moving:
					float distanceToTarget = CenterPosition.DistanceTo(TargetPosition);
					if (distanceToTarget < 15f) // Reached target
					{
						if (Thirst > 0.4f)
						{
							TransitionToState(HerdState.Drinking);
							_timeSinceLastDrink = 0f;
						}
						else
						{
							TransitionToState(HerdState.Grazing);
						}
					}
					break;

				case HerdState.Drinking:
					if (Thirst < 0.2f && _stateTime > 60f) // Hydrated after 1 minute
					{
						TransitionToState(HerdState.Grazing);
					}
					break;

				case HerdState.Resting:
					if (Fatigue < 0.3f && _stateTime > 180f) // Rested after 3 minutes
					{
						_timeSinceLastRest = 0f;
						TransitionToState(Hunger > 0.5f ? HerdState.Grazing : HerdState.Moving);
						if (CurrentState == HerdState.Moving)
							SetRandomTarget();
					}
					break;

				case HerdState.Fleeing:
					if (FearLevel < 0.3f && _stateTime > 30f) // Calmed after 30 seconds
					{
						TransitionToState(HerdState.Alerting);
					}
					break;

				case HerdState.Alerting:
					if (FearLevel < 0.1f && _stateTime > 20f) // Alert for 20 seconds
					{
						TransitionToState(HerdState.Grazing);
					}
					break;
			}
		}

		/// <summary>
		/// Transitions to a new behavioral state.
		/// </summary>
		private void TransitionToState(HerdState newState)
		{
			if (newState == CurrentState) return;

			CurrentState = newState;
			_stateTime = 0f;
			_currentSpreadRadius = GetSpreadRadiusForState(newState);

			// Update all animal animations based on new state
			AnimationState targetAnimation = GetAnimationForState(newState);
			int count = Animals.Length;
			for (int i = 0; i < count; i++)
			{
				Animals[i].CurrentAnimation = targetAnimation;
			}
		}

		/// <summary>
		/// Executes behavior for the current state.
		/// </summary>
		private void ExecuteCurrentState(float deltaTime)
		{
			switch (CurrentState)
			{
				case HerdState.Moving:
					Vector3 direction = TargetPosition - CenterPosition;
					float lengthSq = direction.LengthSquared();

					if (lengthSq > 0.0001f)
					{
						direction /= Mathf.Sqrt(lengthSq);
					}
					else
					{
						direction = Vector3.Zero;
					}
					float moveSpeed = _config.WalkSpeedMPS * deltaTime;
					Vector3 movement = direction * moveSpeed;
					CenterPosition += movement;
					_dailyTravelDistance += movement.Length();
					MovementDirection = direction;
					break;

				case HerdState.Fleeing:
					Vector3 fleeDirection = MovementDirection;
					if (fleeDirection == Vector3.Zero)
					{
						fleeDirection = GetFleeDirection();
						MovementDirection = fleeDirection;
					}
					float fleeSpeed = _config.RunSpeedMPS * deltaTime;
					Vector3 fleeMovement = fleeDirection * fleeSpeed;
					CenterPosition += fleeMovement;
					_dailyTravelDistance += fleeMovement.Length();
					break;

				case HerdState.Grazing:
					// Slow drift while grazing
					Vector3 grazeDirection = GetRandomDirection();
					float grazeSpeed = _config.GrazeSpeedMPS * deltaTime;
					Vector3 grazeMovement = grazeDirection * grazeSpeed;
					CenterPosition += grazeMovement;
					_dailyTravelDistance += grazeMovement.Length();
					break;

				case HerdState.Drinking:
					// Consume water from the water source
					DrinkFromWaterSource(deltaTime);
					MovementDirection = Vector3.Zero;
					break;

				case HerdState.Resting:
				case HerdState.Alerting:
					// Stationary states
					MovementDirection = Vector3.Zero;
					break;
			}

			// Check daily travel limits
			float maxDailyMeters = _config.MaxDailyTravelKm * 1000f;
			if (_dailyTravelDistance > maxDailyMeters)
			{
				Fatigue = Math.Min(1f, Fatigue + 0.2f);
				if (CurrentState == HerdState.Moving)
				{
					TransitionToState(HerdState.Resting);
				}
			}
		}

		/// <summary>
		/// Updates individual animal positions and animations for Full LOD.
		/// </summary>
		private void UpdateIndividualAnimals(float deltaTime)
		{
			int count = Animals.Length;
			for (int i = 0; i < count; i++)
			{
				ref AnimalStruct animal = ref Animals[i];
				if (animal.Health <= 0f) continue;

				// Update variation timer
				animal.NextVariationTime -= deltaTime;
				
				if (animal.NextVariationTime <= 0f)
				{
					// Precompute variation time AND animation variation together
					float variationRoll = (float)_rng.NextDouble();

					animal.NextVariationTime = 2f + variationRoll * 5f;

					// Optional: drive animation variation off same roll (no extra RNG)
					if (variationRoll < 0.3f)
					{
					animal.CurrentAnimation = GetVariedAnimation(animal.CurrentAnimation);
					}
					
					// Apply individual positioning based on cached spread radius
					Vector3 randomOffset = GetRandomOffsetInRadius(_currentSpreadRadius);
					animal.WorldPosition = randomOffset;

					// Use variation timer instead of per-frame RNG
					animal.CurrentAnimation = GetVariedAnimation(animal.CurrentAnimation);
				}
			}
		}

		/// <summary>
		/// Sets target to nearest water source using WaterSystem.
		/// Falls back to random direction if no water found.
		/// </summary>
		private void SetWaterTarget()
		{
			// Query WaterSystem for nearest available water
			WaterSource? nearestWater = WaterSystem.Instance.FindBestWater(CenterPosition, 3000f);

			if (nearestWater.HasValue)
			{
				TargetPosition = nearestWater.Value.Position;
				_targetWaterSourceId = nearestWater.Value.Id;
			}
			else
			{
				// No water found - move in random direction hoping to find some
				Vector3 waterDirection = GetRandomDirection();
				TargetPosition = CenterPosition + waterDirection * 200f;
				_targetWaterSourceId = -1;
			}
		}

		/// <summary>
		/// Called when herd reaches water and drinks.
		/// Consumes water from the WaterSystem.
		/// </summary>
		private void DrinkFromWaterSource(float deltaTime)
		{
			if (_targetWaterSourceId < 0) return;

			// Each animal drinks ~20 liters per drinking session over time
			// Large animals (buffalo) drink more, small animals (impala) less
			float litersPerAnimalPerSecond = 0.5f; // ~30 liters over a minute
			float totalLiters = Animals.Length * litersPerAnimalPerSecond * deltaTime;

			float consumed = WaterSystem.Instance.ConsumeWater(_targetWaterSourceId, totalLiters);

			// If water source ran dry while drinking, need to find new source
			if (consumed < totalLiters * 0.5f)
			{
				WaterSource? source = WaterSystem.Instance.GetWaterSource(_targetWaterSourceId);
				if (!source.HasValue || !source.Value.HasWater)
				{
					// Water dried up - need to move on
					Thirst = Math.Min(1f, Thirst + 0.1f); // Still thirsty
					TransitionToState(HerdState.Moving);
					SetWaterTarget();
				}
			}
		}

		/// <summary>
		/// Sets a random movement target within reasonable range.
		/// </summary>
		private void SetRandomTarget()
		{
			Vector3 randomDirection = GetRandomDirection();
			float distance = (float)(_rng.NextDouble() * 200.0 + 50.0);
			TargetPosition = CenterPosition + randomDirection * distance;
		}

		/// <summary>
		/// Gets flee direction away from last known threat.
		/// </summary>
		private Vector3 GetFleeDirection()
		{
			if (_lastThreatPosition != Vector3.Zero)
			{
				Vector3 direction = (CenterPosition - _lastThreatPosition).Normalized();
				if (direction.LengthSquared() > 0.01f)
					return direction;
			}
			return GetRandomDirection();
		}

		/// <summary>
		/// Generates a random horizontal direction vector.
		/// </summary>
		private Vector3 GetRandomDirection()
		{
			float angle = (float)(_rng.NextDouble() * Math.PI * 2.0);
			return new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
		}

		/// <summary>
		/// Gets random offset within specified radius for animal positioning.
		/// </summary>
		private Vector3 GetRandomOffsetInRadius(float radius)
		{
			float angle = (float)(_rng.NextDouble() * Math.PI * 2.0);
			float distance = (float)(_rng.NextDouble() * radius);
			return new Vector3(
				Mathf.Cos(angle) * distance,
				0f,
				Mathf.Sin(angle) * distance
			);
		}

		/// <summary>
		/// Gets appropriate spread radius for herd state.
		/// </summary>
		private float GetSpreadRadiusForState(HerdState state)
		{
			return state switch
			{
				HerdState.Grazing => 25f,
				HerdState.Resting => 15f,
				HerdState.Fleeing => 8f,
				HerdState.Alerting => 10f,
				HerdState.Drinking => 12f,
				HerdState.Moving => 18f,
				_ => 20f
			};
		}

		/// <summary>
		/// Gets animation state corresponding to herd state.
		/// </summary>
		private AnimationState GetAnimationForState(HerdState state)
		{
			return state switch
			{
				HerdState.Grazing => AnimationState.Grazing,
				HerdState.Moving => AnimationState.Walking,
				HerdState.Drinking => AnimationState.Drinking,
				HerdState.Resting => AnimationState.Resting,
				HerdState.Fleeing => AnimationState.Fleeing,
				HerdState.Alerting => AnimationState.Alert,
				_ => AnimationState.Idle
			};
		}

		/// <summary>
		/// Adds subtle animation variation to prevent uniformity.
		/// </summary>
		private AnimationState GetVariedAnimation(AnimationState baseAnimation)
		{
			return baseAnimation switch
			{
				AnimationState.Grazing => _rng.NextDouble() < 0.3 ? AnimationState.Idle : AnimationState.Grazing,
				AnimationState.Walking => _rng.NextDouble() < 0.2 ? AnimationState.Idle : AnimationState.Walking,
				_ => baseAnimation
			};
		}

		/// <summary>
		/// Alerts herd to a threat at specified position.
		/// Called by external systems (predator detection, gunshots, etc.).
		/// </summary>
		/// <param name="threatPosition">World position of threat</param>
		/// <param name="threatIntensity">Intensity from 0.0 to 1.0</param>
		public void AlertToThreat(Vector3 threatPosition, float threatIntensity)
		{
			_lastThreatPosition = threatPosition;
			FearLevel = Math.Clamp(FearLevel + threatIntensity, 0f, 1f);

			if (FearLevel > 0.7f)
			{
				TransitionToState(HerdState.Fleeing);
				MovementDirection = GetFleeDirection();
			}
			else if (FearLevel > 0.3f)
			{
				TransitionToState(HerdState.Alerting);
			}
		}

		/// <summary>
		/// Daily tick for resetting daily counters and health updates.
		/// Called by TimeSystem.
		/// </summary>
		public void OnDailyTick()
		{
			// Reset daily travel counter
			_dailyTravelDistance = 0f;
			_lastDayPosition = CenterPosition;

			// Health effects from critical needs
			if (Thirst > 0.8f || Hunger > 0.8f)
			{
				float healthLoss = 0.02f; // 2% health loss per day
				int count = Animals.Length;
				for (int i = 0; i < count; i++)
				{
					ref AnimalStruct animal = ref Animals[i];
					animal.Health = Math.Max(0f, animal.Health - healthLoss);
				}
			}
		}

		/// <summary>
		/// Monthly tick for long-term simulation and population dynamics.
		/// Called by TimeSystem.
		/// </summary>
		public void OnMonthlyTick()
		{
			// Age all animals
			int count = Animals.Length;
			for (int i = 0; i < count; i++)
			{
				ref AnimalStruct animal = ref Animals[i];
				animal.Age += 1f; // One month older

				// Natural death from old age (15+ years)
				if (animal.Age > 180f)
				{
					float deathChance = (animal.Age - 180f) / 60f; // 0-100% over 5 years
					if (_rng.NextDouble() < deathChance)
					{
						animal.Health = 0f;
						// TODO: Handle death - notify other systems, update population stats
					}
				}
			}

			// Background herds still accumulate some needs
			if (CurrentLOD == BehaviourLOD.Background)
			{
				Thirst = Math.Min(1f, Thirst + 0.3f);
				Hunger = Math.Min(1f, Hunger + 0.2f);
			}

			// TODO: Breeding logic, migration patterns, seasonal behavior changes
		}

		private void TryReproduce()
		{
			int maleCount = 0;
			int femaleCount = 0;

			for (int i = 0; i < Animals.Length; i++)
			{
				if (Animals[i].Health <= 0f) continue;

				if (Animals[i].Sex == AnimalSex.Male) maleCount++;
				else femaleCount++;
			}

			// Need both sexes
			if (maleCount == 0 || femaleCount == 0)
				return;

			// Limit population growth
			int maxNew = Math.Min(femaleCount / 2, 5); // cap births per cycle

			if (maxNew <= 0)
				return;

			int spawnCount = _rng.Next(1, maxNew + 1);

			SpawnOffspring(spawnCount);
		}

		private void SpawnOffspring(int count)
		{
			int currentCount = Animals.Length;
			int newCount = currentCount + count;

			AnimalStruct[] newAnimals = Animals;
			Array.Resize(ref newAnimals, newCount);
			Animals = newAnimals;

			for (int i = currentCount; i < newCount; i++)
			{
				AnimalStruct baby = new AnimalStruct();

				baby.Sex = _rng.NextDouble() < 0.5 ? AnimalSex.Male : AnimalSex.Female;
				baby.Age = 0f;
				baby.Health = 1f;

				// WorldPosition in AnimalStruct is an offset relative to CenterPosition
				baby.WorldPosition = GetRandomOffsetInRadius(2f);
				baby.CurrentAnimation = AnimationState.Idle;
				baby.MeshInstanceId = -1;
				baby.UniqueId = AnimalSystem.Instance.GetNextAnimalId();
				baby.NextVariationTime = (float)(_rng.NextDouble() * 5.0 + 2.0);

				// Implement Genetics Inheritance
				baby.Genetics = Animals[_rng.Next(currentCount)].Genetics;

				Animals[i] = baby;
			}
		}
	}
}
