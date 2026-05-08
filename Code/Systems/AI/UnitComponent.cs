using Sandbox;
using Sandbox.Network;

namespace Sandbox.Systems.AI;

public sealed class UnitComponent : Component, Component.IDamageable
{
	private const float DeathPresentationDelay = 0.25f;

	/// <summary>
	/// Displayed name
	/// </summary>
	[Property]
	public string Name { get; set; }

	/// <summary>
	/// Which team this unit belongs to
	/// </summary>
	[Property]
	public TeamType Team { get; set; }

	/// <summary>
	/// What the max health is
	/// </summary>
	[Property]
	[Range( 10f, 300f )]
	[Step( 10f )]
	public float MaxHealth { get; set; } = 100f;

	/// <summary>
	/// Maximum stamina. Stamina is host-authoritative and regenerates over time.
	/// </summary>
	[Property]
	[Range( 0f, 300f )]
	[Step( 10f )]
	public float MaxStamina { get; set; } = 100f;

	/// <summary>
	/// Health points regenerated per minute by the host.
	/// </summary>
	[Property]
	[Range( 0f, 100f )]
	[Step( 1f )]
	public float HealthRegenPerMinute { get; set; } = 0f;

	/// <summary>
	/// Stamina regenerated per second by the host.
	/// </summary>
	[Property]
	[Range( 0f, 100f )]
	[Step( 1f )]
	public float StaminaRegenRate { get; set; } = 10f;

	/// <summary>
	/// Stamina drained per second while the owner holds the run input.
	/// </summary>
	[Property]
	[Range( 0f, 20f )]
	[Step( 0.1f )]
	public float RunStaminaDrainRate { get; set; } = 1f;

	/// <summary>
	/// Maximum armor pool. Armor absorbs incoming positive damage before health.
	/// </summary>
	[Property]
	[Range( 0f, 300f )]
	[Step( 10f )]
	public float MaxArmor { get; set; } = 0f;

	/// <summary>
	/// Money paid to the player who lands the killing blow on this unit. 0 = no payout.
	/// Paid by the host when this unit dies.
	/// </summary>
	[Property]
	[Range( 0, 1000 )]
	[Step( 10 )]
	public int Bounty { get; set; } = 0;

	[Property]
	[Range( 0f, 300f )]
	[Step( 1f )]
	public float CorpseLifetimeSeconds { get; set; } = 60f;

	[Property]
	public SkinnedModelRenderer ModelRenderer { get; set; }

	/// <summary>
	/// Optional ModelPhysics component. If assigned, MotionEnabled flips to true on death so the unit ragdolls.
	/// </summary>
	[Property]
	public ModelPhysics Physics { get; set; }

	/// <summary>
	/// Optional PatrolComponent. If assigned, it is disabled on death so the corpse stops walking.
	/// </summary>
	[Property]
	public PatrolComponent Patrol { get; set; }

	/// <summary>
	/// Current health. Host-authoritative — clients receive updates via Sync.
	/// Mutate only through <see cref="OnDamage"/>.
	/// </summary>
	[Sync( SyncFlags.FromHost )]
	public float Health { get; set; }

	/// <summary>
	/// Current stamina. Host-authoritative; clients receive updates via Sync.
	/// </summary>
	[Sync( SyncFlags.FromHost )]
	public float Stamina { get; set; }

	/// <summary>
	/// Current armor pool. Host-authoritative; positive damage drains this before health.
	/// </summary>
	[Sync( SyncFlags.FromHost )]
	public float Armor { get; set; }

	[Sync( SyncFlags.FromHost )]
	public bool IsDead { get; set; }

	[Sync( SyncFlags.FromHost )]
	public float DeathTime { get; set; }

	[Sync( SyncFlags.FromHost )]
	public float Hydration { get; set; }

	[Sync( SyncFlags.FromHost )]
	public float Nutrition { get; set; }

	[Sync( SyncFlags.FromHost )]
	public bool IsThirsty { get; set; }

	[Sync( SyncFlags.FromHost )]
	public bool IsHungry { get; set; }

	private float _lastHealth;
	private GameObject _lastAttacker;
	private PlayerStatsComponent _stats;
	private float _lastAppliedMaxHealth;
	private float _lastAppliedMaxStamina;
	private int _lastAppliedStaminaLevel;
	private bool _wantsRunStaminaDrain;
	private bool _isRunStaminaDrainActive;
	private bool _sprintLocked;
	private bool _bountyPaid;
	private bool _bountyAttempted;
	private bool _deathLogged;
	private bool _deathControlsApplied;
	private bool _deathEquipmentDisabled;
	private bool _deathPatrolDisabled;
	private bool _deathPresentationApplied;
	private bool _deathPresentationWarningLogged;
	private bool _corpseCleanedUp;
	private float _deathObservedTime = -1f;
	private float _pendingHealthRegen;
	private float _healthRegenModifierPerSecond;
	private float _healthRegenModifierEndTime;
	private float _staminaRegenModifierPerSecond;
	private float _staminaRegenModifierEndTime;
	private GameObject _networkedCorpse;
	private bool _networkedCorpseWarningLogged;

	[Hide] public float EffectiveMaxHealth => ResolvePlayerStats()?.EffectiveMaxHealth ?? MaxHealth;
	[Hide] public float EffectiveMaxStamina => ResolvePlayerStats()?.EffectiveMaxStamina ?? MaxStamina;
	[Hide] public bool IsRunStaminaDrainActive => _isRunStaminaDrainActive;
	[Hide] public bool HasStaminaToStartRunning => !IsDead && Stamina > 0f && Stamina >= EffectiveSprintResumeStamina;
	[Hide] public bool HasStaminaToContinueRunning => !IsDead && Stamina > 0f;

	[Group( "Debug" ), Order( 100 )]
	[Button( "Hurt 10", "success" )]
	public void HurtDebug() => OnDamage( new DamageInfo { Damage = MaxHealth } );

	[Group( "Debug" ), Order( 101 )]
	[Button( "Heal 10", "favorite" )]
	public void HealDebug() => OnDamage( new DamageInfo { Damage = -10f } );

	[Group( "Debug" ), Order( 102 )]
	[Button( "Make Thirsty", "water_drop" )]
	public void MakeThirstyDebug()
	{
		var target = DebugTargetUnit();
		if ( !target.IsValid() )
		{
			Log.Warning( $"{Name}: Make Thirsty failed - no runtime UnitComponent was found." );
			return;
		}

		if ( !Sandbox.Networking.IsHost )
		{
			Log.Warning( $"{target.Name}: Make Thirsty ignored - needs are host-authoritative." );
			return;
		}

		target.SetThirstyDebug();
	}

	[Group( "Debug" ), Order( 103 )]
	[Button( "Make Hungry", "restaurant" )]
	public void MakeHungryDebug()
	{
		var target = DebugTargetUnit();
		if ( !target.IsValid() )
		{
			Log.Warning( $"{Name}: Make Hungry failed - no runtime UnitComponent was found." );
			return;
		}

		if ( !Sandbox.Networking.IsHost )
		{
			Log.Warning( $"{target.Name}: Make Hungry ignored - needs are host-authoritative." );
			return;
		}

		target.SetHungryDebug();
	}

	[Group( "Debug" ), Order( 104 )]
	[Button( "Drink", "local_drink" )]
	public void DrinkDebug()
	{
		var target = DebugTargetUnit();
		if ( !target.IsValid() )
		{
			Log.Warning( $"{Name}: Drink failed - no runtime UnitComponent was found." );
			return;
		}

		if ( !Sandbox.Networking.IsHost )
		{
			Log.Warning( $"{target.Name}: Drink ignored - needs are host-authoritative." );
			return;
		}

		target.SetDrankDebug();
	}

	[Group( "Debug" ), Order( 105 )]
	[Button( "Eat", "lunch_dining" )]
	public void EatDebug()
	{
		var target = DebugTargetUnit();
		if ( !target.IsValid() )
		{
			Log.Warning( $"{Name}: Eat failed - no runtime UnitComponent was found." );
			return;
		}

		if ( !Sandbox.Networking.IsHost )
		{
			Log.Warning( $"{target.Name}: Eat ignored - needs are host-authoritative." );
			return;
		}

		target.SetAteDebug();
	}

	/// <summary>
	/// Apply damage. Negative <see cref="DamageInfo.Damage"/> heals.
	/// Host-only — proxies wait for the synced <see cref="Health"/> update.
	/// </summary>
	public void OnDamage( in DamageInfo info )
	{
		if ( !Sandbox.Networking.IsHost ) return;
		if ( IsDead ) return;

		var incoming = info.Damage;

		// Track the last hostile hitter so Die() can credit the kill. Heals (negative
		// damage) shouldn't overwrite — otherwise a friendly heal could rob the killer.
		if ( incoming > 0f )
		{
			_lastAttacker = info.Attacker;
			incoming = AbsorbDamageWithArmor( incoming );
		}

		Health = float.Clamp( Health - incoming, 0f, EffectiveMaxHealth );

		if ( Health <= 0f )
		{
			MarkDead();
			TryPayBounty();
		}
	}

	protected override void OnStart()
	{
		ResolvePlayerStats();

		if ( Sandbox.Networking.IsHost )
		{
			Health = EffectiveMaxHealth;
			Stamina = EffectiveMaxStamina;
			Armor = MaxArmor;
			Hydration = EffectiveMaxHydration;
			Nutrition = EffectiveMaxNutrition;
			UpdateNeedsState();
			IsDead = false;
			DeathTime = 0f;
		}

		_lastAppliedMaxHealth = EffectiveMaxHealth;
		_lastAppliedMaxStamina = EffectiveMaxStamina;
		_lastAppliedStaminaLevel = ResolvePlayerStats()?.StaminaLevel ?? 1;
		_lastHealth = Health;
		ResetDeathPresentationState();
	}

	protected override void OnUpdate()
	{
		if ( Sandbox.Networking.IsHost && !IsDead )
		{
			TickNeeds();
			ApplyStatDerivedPools();
			TickConsumableModifiers();
			RegenerateHealth();
			UpdateSprintLock();

			_isRunStaminaDrainActive = false;
			if ( _wantsRunStaminaDrain && !_sprintLocked && Stamina > 0f ) DrainRunStamina();
			else RegenerateStamina();
		}

		TickDeathState();

		if ( Health == _lastHealth ) return;

		var difference = Health - _lastHealth;
		Log.Info( $"{Name} health changed by {difference:F0}, new health is {Health:F0}" );
		_lastHealth = Health;
	}

	public bool TrySpendStamina( float amount )
	{
		if ( !Sandbox.Networking.IsHost ) return false;
		if ( amount <= 0f ) return true;
		if ( Stamina < amount ) return false;

		Stamina = float.Clamp( Stamina - amount, 0f, EffectiveMaxStamina );
		if ( Stamina <= 0f ) _sprintLocked = true;
		return true;
	}

	public void RestoreStamina( float amount )
	{
		if ( !Sandbox.Networking.IsHost ) return;
		if ( amount <= 0f ) return;

		Stamina = float.Clamp( Stamina + amount, 0f, EffectiveMaxStamina );
	}

	public void SetArmor( float amount )
	{
		if ( !Sandbox.Networking.IsHost ) return;

		Armor = float.Clamp( amount, 0f, MaxArmor );
	}

	public void RestoreHydration( float amount )
	{
		if ( !Sandbox.Networking.IsHost ) return;
		if ( amount <= 0f ) return;

		Hydration = float.Clamp( Hydration + amount, 0f, EffectiveMaxHydration );
		UpdateNeedsState();
	}

	public void RestoreNutrition( float amount )
	{
		if ( !Sandbox.Networking.IsHost ) return;
		if ( amount <= 0f ) return;

		Nutrition = float.Clamp( Nutrition + amount, 0f, EffectiveMaxNutrition );
		UpdateNeedsState();
	}

	public bool TryApplyConsumable( ItemDefinition definition )
	{
		if ( !Sandbox.Networking.IsHost ) return false;
		if ( IsDead ) return false;
		if ( definition?.IsConsumable != true ) return false;

		var consumable = definition.Consumable;
		if ( consumable is null ) return false;

		AdjustHealth( consumable.Health );
		if ( IsDead ) return true;

		AdjustStamina( consumable.Stamina );
		AdjustHydration( consumable.Hydration );
		AdjustNutrition( consumable.Nutrition );
		UpdateNeedsState();

		ApplyTimedConsumableModifiers( consumable );
		return true;
	}

	private void RegenerateHealth()
	{
		var maxHealth = EffectiveMaxHealth;
		var regenPerMinute = EffectiveHealthRegenPerMinute;
		if ( regenPerMinute <= 0f ) return;

		if ( Health >= maxHealth )
		{
			_pendingHealthRegen = 0f;
			return;
		}

		_pendingHealthRegen += regenPerMinute * Time.Delta / 60f;
		var wholeHealth = (float)System.Math.Floor( _pendingHealthRegen );
		if ( wholeHealth < 1f ) return;

		_pendingHealthRegen -= wholeHealth;
		Health = float.Clamp( Health + wholeHealth, 0f, maxHealth );
	}

	private void TickNeeds()
	{
		var maxHydration = EffectiveMaxHydration;
		var maxNutrition = EffectiveMaxNutrition;

		if ( maxHydration > 0f )
		{
			Hydration = float.Clamp( Hydration - EffectiveHydrationDrainPerMinute * Time.Delta / 60f, 0f, maxHydration );
		}

		if ( maxNutrition > 0f )
		{
			Nutrition = float.Clamp( Nutrition - EffectiveNutritionDrainPerMinute * Time.Delta / 60f, 0f, maxNutrition );
		}

		UpdateNeedsState();
	}

	private void UpdateNeedsState()
	{
		var maxHydration = EffectiveMaxHydration;
		var maxNutrition = EffectiveMaxNutrition;

		IsThirsty = maxHydration > 0f && Hydration <= EffectiveThirstyThreshold;
		IsHungry = maxNutrition > 0f && Nutrition <= EffectiveHungryThreshold;
	}

	private void AdjustHealth( float amount )
	{
		if ( amount == 0f ) return;

		if ( amount < 0f ) _lastAttacker = null;
		Health = float.Clamp( Health + amount, 0f, EffectiveMaxHealth );
		if ( Health > 0f ) return;

		MarkDead();
		TryPayBounty();
	}

	private void MarkDead()
	{
		if ( IsDead ) return;

		IsDead = true;
		DeathTime = Time.Now;
		var attacker = _lastAttacker.IsValid() ? _lastAttacker.Root : null;
		GameLogSystem.Current?.Info( "combat", "Unit killed", attacker, data: GameLogSystem.Fields(
			("target", PlayerLogName( GameObject.Root )),
			("targetComponent", Name),
			("team", Team),
			("bounty", Bounty),
			("attacker", PlayerLogName( attacker )) ) );
		TryCreateNetworkedPlayerCorpse();
	}

	private void AdjustStamina( float amount )
	{
		if ( amount == 0f ) return;

		Stamina = float.Clamp( Stamina + amount, 0f, EffectiveMaxStamina );
		if ( Stamina <= 0f ) _sprintLocked = true;
		else if ( _sprintLocked && Stamina >= EffectiveSprintResumeStamina ) _sprintLocked = false;
	}

	private void AdjustHydration( float amount )
	{
		if ( amount == 0f ) return;

		Hydration = float.Clamp( Hydration + amount, 0f, EffectiveMaxHydration );
	}

	private void AdjustNutrition( float amount )
	{
		if ( amount == 0f ) return;

		Nutrition = float.Clamp( Nutrition + amount, 0f, EffectiveMaxNutrition );
	}

	private void ApplyTimedConsumableModifiers( ConsumableStats consumable )
	{
		// Same-stat consumable modifiers replace each other; health and stamina can coexist.
		ApplyTimedModifier( consumable.HealthRegenPerSecond, consumable.EffectDuration, ref _healthRegenModifierPerSecond, ref _healthRegenModifierEndTime );
		ApplyTimedModifier( consumable.StaminaRegenPerSecond, consumable.EffectDuration, ref _staminaRegenModifierPerSecond, ref _staminaRegenModifierEndTime );
	}

	private void ApplyTimedModifier( float amountPerSecond, float duration, ref float currentAmount, ref float endTime )
	{
		if ( duration <= 0f ) return;
		if ( System.MathF.Abs( amountPerSecond ) <= 0.001f ) return;

		currentAmount = amountPerSecond;
		endTime = Time.Now + duration;
	}

	private void TickConsumableModifiers()
	{
		ClearTimedModifierIfExpired( ref _healthRegenModifierPerSecond, ref _healthRegenModifierEndTime );
		ClearTimedModifierIfExpired( ref _staminaRegenModifierPerSecond, ref _staminaRegenModifierEndTime );
	}

	private void ClearTimedModifierIfExpired( ref float currentAmount, ref float endTime )
	{
		if ( endTime <= 0f ) return;
		if ( Time.Now < endTime ) return;

		currentAmount = 0f;
		endTime = 0f;
	}

	private UnitComponent DebugTargetUnit()
	{
		if ( GameObject.Enabled && Sandbox.Systems.Movement.LocalPlayer.Owns( GameObject ) ) return this;

		var localUnit = Sandbox.Systems.Movement.LocalPlayer.Component<UnitComponent>( Scene );
		if ( localUnit.IsValid() ) return localUnit;

		return GameObject.Enabled ? this : null;
	}

	private void SetThirstyDebug()
	{
		var before = Hydration;
		Hydration = EffectiveThirstyThreshold;
		UpdateNeedsState();
		Log.Info( $"{Name}: debug thirst applied, hydration {before:F1} -> {Hydration:F1}, thirsty={IsThirsty}." );
	}

	private void SetHungryDebug()
	{
		var before = Nutrition;
		Nutrition = EffectiveHungryThreshold;
		UpdateNeedsState();
		Log.Info( $"{Name}: debug hunger applied, nutrition {before:F1} -> {Nutrition:F1}, hungry={IsHungry}." );
	}

	private void SetDrankDebug()
	{
		var before = Hydration;
		RestoreHydration( EffectiveMaxHydration );
		Log.Info( $"{Name}: debug drink applied, hydration {before:F1} -> {Hydration:F1}, thirsty={IsThirsty}." );
	}

	private void SetAteDebug()
	{
		var before = Nutrition;
		RestoreNutrition( EffectiveMaxNutrition );
		Log.Info( $"{Name}: debug eat applied, nutrition {before:F1} -> {Nutrition:F1}, hungry={IsHungry}." );
	}

	public void SetRunStaminaDrain( bool running )
	{
		if ( !Sandbox.Networking.IsHost ) return;

		if ( running && !CanStartRunStaminaDrain() )
		{
			_wantsRunStaminaDrain = false;
			return;
		}

		_wantsRunStaminaDrain = running;
	}

	private float AbsorbDamageWithArmor( float damage )
	{
		if ( damage <= 0f || Armor <= 0f ) return damage;

		var absorbed = float.Min( Armor, damage );
		Armor = float.Clamp( Armor - absorbed, 0f, MaxArmor );
		return damage - absorbed;
	}

	private void RegenerateStamina()
	{
		var maxStamina = EffectiveMaxStamina;
		var regenRate = EffectiveStaminaRegenRate;
		if ( regenRate <= 0f ) return;
		if ( Stamina >= maxStamina ) return;

		Stamina = float.Clamp( Stamina + regenRate * Time.Delta, 0f, maxStamina );
	}

	private void DrainRunStamina()
	{
		var drainRate = EffectiveRunStaminaDrainRate;
		if ( drainRate <= 0f ) return;
		if ( Stamina <= 0f ) return;

		_isRunStaminaDrainActive = true;
		Stamina = float.Clamp( Stamina - drainRate * Time.Delta, 0f, EffectiveMaxStamina );
		if ( Stamina > 0f ) return;

		_sprintLocked = true;
		_wantsRunStaminaDrain = false;
	}

	private bool CanStartRunStaminaDrain()
	{
		if ( IsDead ) return false;
		if ( _sprintLocked && Stamina < EffectiveSprintResumeStamina ) return false;

		return Stamina > 0f && Stamina >= EffectiveSprintResumeStamina;
	}

	private void UpdateSprintLock()
	{
		if ( Stamina <= 0f )
		{
			_sprintLocked = true;
			return;
		}

		if ( _sprintLocked && Stamina >= EffectiveSprintResumeStamina )
		{
			_sprintLocked = false;
		}
	}

	private void ApplyStatDerivedPools()
	{
		var stats = ResolvePlayerStats();
		var maxHealth = EffectiveMaxHealth;
		var maxStamina = EffectiveMaxStamina;
		var staminaLevel = stats?.StaminaLevel ?? 1;

		if ( _lastAppliedMaxHealth <= 0f ) _lastAppliedMaxHealth = maxHealth;
		if ( _lastAppliedMaxStamina <= 0f ) _lastAppliedMaxStamina = maxStamina;
		if ( _lastAppliedStaminaLevel <= 0 ) _lastAppliedStaminaLevel = staminaLevel;

		if ( maxHealth != _lastAppliedMaxHealth )
		{
			Health = float.Clamp( Health, 0f, maxHealth );
			_lastAppliedMaxHealth = maxHealth;
		}

		if ( staminaLevel > _lastAppliedStaminaLevel )
		{
			Stamina = maxStamina;
			_sprintLocked = false;
		}
		else if ( maxStamina != _lastAppliedMaxStamina )
		{
			var delta = maxStamina - _lastAppliedMaxStamina;
			Stamina = delta > 0f
				? float.Clamp( Stamina + delta, 0f, maxStamina )
				: float.Clamp( Stamina, 0f, maxStamina );
		}

		_lastAppliedStaminaLevel = staminaLevel;
		_lastAppliedMaxStamina = maxStamina;
	}

	private PlayerStatsComponent ResolvePlayerStats()
	{
		if ( _stats.IsValid() ) return _stats;

		_stats = GameObject.Root.Components.GetInDescendantsOrSelf<PlayerStatsComponent>();
		return _stats;
	}

	private float EffectiveHealthRegenPerMinute => ((ResolvePlayerStats()?.HealthRegenPerMinute ?? HealthRegenPerMinute) * (IsHungry ? EffectiveHungryHealthRegenMultiplier : 1f)) + (_healthRegenModifierPerSecond * 60f);
	private float EffectiveStaminaRegenRate => ((ResolvePlayerStats()?.StaminaRegenPerSecond ?? StaminaRegenRate) * (IsThirsty ? EffectiveThirstyStaminaRegenMultiplier : 1f)) + _staminaRegenModifierPerSecond;
	private float EffectiveRunStaminaDrainRate => ResolvePlayerStats()?.RunStaminaDrainPerSecond ?? RunStaminaDrainRate;
	private float EffectiveSprintResumeStamina => float.Max( 0f, ResolvePlayerStats()?.SprintResumeStamina ?? 0f );
	private float EffectiveMaxHydration => ResolvePlayerStats()?.MaxHydration ?? 0f;
	private float EffectiveMaxNutrition => ResolvePlayerStats()?.MaxNutrition ?? 0f;
	private float EffectiveHydrationDrainPerMinute => float.Max( 0f, ResolvePlayerStats()?.HydrationDrainPerMinute ?? 0f );
	private float EffectiveNutritionDrainPerMinute => float.Max( 0f, ResolvePlayerStats()?.NutritionDrainPerMinute ?? 0f );
	private float EffectiveThirstyThreshold => float.Clamp( ResolvePlayerStats()?.ThirstyThreshold ?? 0f, 0f, EffectiveMaxHydration );
	private float EffectiveHungryThreshold => float.Clamp( ResolvePlayerStats()?.HungryThreshold ?? 0f, 0f, EffectiveMaxNutrition );
	private float EffectiveThirstyStaminaRegenMultiplier => float.Clamp( ResolvePlayerStats()?.ThirstyStaminaRegenMultiplier ?? 1f, 0f, 1f );
	private float EffectiveHungryHealthRegenMultiplier => float.Clamp( ResolvePlayerStats()?.HungryHealthRegenMultiplier ?? 1f, 0f, 1f );

	private void TickDeathState()
	{
		if ( !IsDead ) return;

		if ( _deathObservedTime < 0f ) _deathObservedTime = Time.Now;

		if ( !_deathLogged )
		{
			_deathLogged = true;
			Log.Info( $"{Name} died" );
		}

		if ( !_deathControlsApplied )
		{
			DisablePlayerControlOnDeath();
			_deathControlsApplied = true;
		}

		if ( !_deathEquipmentDisabled )
		{
			DisableEquipmentViewsOnDeath();
			_deathEquipmentDisabled = true;
		}

		if ( !_deathPatrolDisabled )
		{
			if ( Patrol.IsValid() ) Patrol.Enabled = false;
			_deathPatrolDisabled = true;
		}

		TryCreateNetworkedPlayerCorpse();

		TryPayBounty();
		TickDeathPresentation();
	}

	private void ResetDeathPresentationState()
	{
		_bountyAttempted = false;
		_deathLogged = false;
		_deathControlsApplied = false;
		_deathEquipmentDisabled = false;
		_deathPatrolDisabled = false;
		_deathPresentationApplied = false;
		_deathPresentationWarningLogged = false;
		_networkedCorpseWarningLogged = false;
		_corpseCleanedUp = false;
		_deathObservedTime = -1f;
	}

	private void TickDeathPresentation()
	{
		if ( _corpseCleanedUp ) return;

		if ( ShouldCleanupCorpse() )
		{
			CleanupCorpse();
			return;
		}

		if ( _deathPresentationApplied ) return;
		if ( DeathElapsed < DeathPresentationDelay || DeathObservedElapsed < DeathPresentationDelay ) return;

		if ( Team == TeamType.Player )
		{
			HideLivingSkinnedRenderers();
			_deathPresentationApplied = true;
			return;
		}

		ApplyNonPlayerDeathPresentation();
		_deathPresentationApplied = true;
	}

	private float DeathElapsed
	{
		get
		{
			var startTime = DeathTime > 0f ? DeathTime : _deathObservedTime;
			if ( startTime < 0f ) return 0f;

			return float.Max( 0f, Time.Now - startTime );
		}
	}

	private float DeathObservedElapsed => _deathObservedTime < 0f ? 0f : float.Max( 0f, Time.Now - _deathObservedTime );

	private bool ShouldCleanupCorpse()
	{
		return DeathElapsed >= float.Max( 0f, CorpseLifetimeSeconds );
	}

	private void CleanupCorpse()
	{
		HideLivingSkinnedRenderers();
		_deathPresentationApplied = true;
		_corpseCleanedUp = true;
	}

	private void ApplyNonPlayerDeathPresentation()
	{
		var bodyRenderer = ResolveBodyRenderer();
		if ( bodyRenderer.IsValid() ) bodyRenderer.UseAnimGraph = false;

		var deathPhysics = ResolveDeathPhysics();
		if ( deathPhysics.IsValid() )
		{
			deathPhysics.MotionEnabled = true;
			return;
		}

		if ( _deathPresentationWarningLogged ) return;

		_deathPresentationWarningLogged = true;
		Log.Warning( $"{Name}: Physics property is not assigned - cannot ragdoll on death" );
	}

	private void TryCreateNetworkedPlayerCorpse()
	{
		if ( !Sandbox.Networking.IsHost ) return;
		if ( Team != TeamType.Player ) return;
		if ( _networkedCorpse.IsValid() ) return;

		var bodyRenderer = ResolveBodyRenderer();
		if ( !bodyRenderer.IsValid() || bodyRenderer.Model is null )
		{
			if ( DeathObservedElapsed >= 2f ) WarnNetworkedCorpseFailed( "body renderer is not ready" );
			return;
		}

		var corpse = CreateNetworkedPlayerCorpseObject( bodyRenderer );
		if ( !corpse.IsValid() )
		{
			WarnNetworkedCorpseFailed( "corpse object could not be created" );
			return;
		}

		if ( Sandbox.Networking.IsActive && !corpse.NetworkSpawn() )
		{
			corpse.Destroy();
			WarnNetworkedCorpseFailed( "NetworkSpawn failed" );
			return;
		}

		_networkedCorpse = corpse;
	}

	private GameObject CreateNetworkedPlayerCorpseObject( SkinnedModelRenderer bodyRenderer )
	{
		var corpse = new GameObject( $"{Name} Corpse" );
		corpse.WorldTransform = bodyRenderer.GameObject.WorldTransform;
		ConfigureCorpseNetworkObject( corpse );

		var corpseBody = corpse.Components.Create<SkinnedModelRenderer>();
		CopyCorpseRenderer( bodyRenderer, corpseBody );

		var physics = corpse.Components.Create<ModelPhysics>();
		physics.Renderer = corpseBody;
		physics.MotionEnabled = true;

		var lifetime = corpse.Components.Create<CorpseLifetimeComponent>();
		lifetime.LifetimeSeconds = CorpseLifetimeSeconds;

		CopyCorpseClothing( corpse, corpseBody, bodyRenderer );
		return corpse;
	}

	private void CopyCorpseClothing( GameObject corpse, SkinnedModelRenderer corpseBody, SkinnedModelRenderer bodyRenderer )
	{
		var root = GameObject.Root;
		if ( !root.IsValid() ) return;

		foreach ( var sourceRenderer in root.Components.GetAll<SkinnedModelRenderer>( FindMode.EverythingInSelfAndDescendants ) )
		{
			if ( !sourceRenderer.IsValid() || sourceRenderer == bodyRenderer || sourceRenderer.Model is null ) continue;

			var clothingObject = new GameObject( $"Corpse Clothing - {sourceRenderer.GameObject.Name}" );
			clothingObject.SetParent( corpse, false );
			clothingObject.LocalPosition = Vector3.Zero;
			clothingObject.LocalRotation = Rotation.Identity;
			clothingObject.LocalScale = Vector3.One;
			ConfigureCorpseNetworkObject( clothingObject );

			var corpseClothing = clothingObject.Components.Create<SkinnedModelRenderer>();
			CopyCorpseRenderer( sourceRenderer, corpseClothing );
			corpseClothing.BoneMergeTarget = corpseBody;
		}
	}

	private static void CopyCorpseRenderer( SkinnedModelRenderer source, SkinnedModelRenderer target )
	{
		target.CopyFrom( source );
		target.Enabled = true;
		target.UseAnimGraph = false;
	}

	private static void ConfigureCorpseNetworkObject( GameObject gameObject )
	{
		if ( !gameObject.IsValid() ) return;

		gameObject.NetworkMode = NetworkMode.Object;
		gameObject.Network.SetOwnerTransfer( OwnerTransfer.Fixed );
	}

	private void WarnNetworkedCorpseFailed( string reason )
	{
		if ( _networkedCorpseWarningLogged ) return;

		_networkedCorpseWarningLogged = true;
		Log.Warning( $"{Name}: player corpse was not spawned because {reason}." );
	}

	private SkinnedModelRenderer ResolveBodyRenderer()
	{
		if ( ModelRenderer.IsValid() && ModelRenderer.Model is not null ) return ModelRenderer;

		var root = GameObject.Root;
		if ( !root.IsValid() ) return ModelRenderer;

		var equipment = root.Components.GetInDescendantsOrSelf<Equipment>();
		if ( equipment.IsValid() && equipment.BodyRenderer.IsValid() && equipment.BodyRenderer.Model is not null )
		{
			ModelRenderer = equipment.BodyRenderer;
			return ModelRenderer;
		}

		foreach ( var renderer in root.Components.GetAll<SkinnedModelRenderer>( FindMode.EverythingInSelfAndDescendants ) )
		{
			if ( !renderer.IsValid() || renderer.Model is null ) continue;

			ModelRenderer = renderer;
			return ModelRenderer;
		}

		return ModelRenderer;
	}

	private void HideLivingSkinnedRenderers()
	{
		var root = GameObject.Root;
		if ( !root.IsValid() ) return;

		foreach ( var renderer in root.Components.GetAll<SkinnedModelRenderer>( FindMode.EverythingInSelfAndDescendants ) )
		{
			renderer.Enabled = false;
		}
	}

	private void DisableEquipmentViewsOnDeath()
	{
		var root = GameObject.Root;
		if ( !root.IsValid() ) return;

		foreach ( var equipment in root.Components.GetAll<Equipment>( FindMode.EnabledInSelfAndDescendants ) )
		{
			equipment.Enabled = false;
		}
	}

	private ModelPhysics ResolveDeathPhysics()
	{
		if ( Physics.IsValid() ) return Physics;

		var bodyRenderer = ResolveBodyRenderer();
		if ( !bodyRenderer.IsValid() ) return null;

		var physics = bodyRenderer.GameObject.Components.Get<ModelPhysics>();
		if ( !physics.IsValid() ) physics = bodyRenderer.GameObject.Components.Create<ModelPhysics>();

		physics.Renderer = bodyRenderer;
		physics.MotionEnabled = false;
		Physics = physics;
		return physics;
	}

	private void DisablePlayerControlOnDeath()
	{
		if ( Team != TeamType.Player ) return;

		var root = GameObject.Root;
		if ( !root.IsValid() ) return;

		var controller = root.Components.Get<PlayerController>();
		if ( controller.IsValid() )
		{
			controller.UseInputControls = false;
			controller.UseLookControls = false;
		}

		var player = root.Components.Get<PlayerComponent>();
		if ( player.IsValid() ) player.Enabled = false;
	}

	private void TryPayBounty()
	{
		if ( !Sandbox.Networking.IsHost ) return;
		if ( _bountyAttempted ) return;
		if ( _bountyPaid ) return;

		_bountyAttempted = true;

		if ( Bounty <= 0 )
		{
			Log.Info( $"{Name}: no bounty paid because Bounty is {Bounty}" );
			return;
		}
		if ( !_lastAttacker.IsValid() )
		{
			Log.Warning( $"{Name}: no bounty paid because last attacker is invalid" );
			return;
		}

		var killerBackpack = FindKillerBackpack();
		if ( !killerBackpack.IsValid() )
		{
			Log.Warning( $"{Name}: no bounty paid because attacker {_lastAttacker.Name} has no Backpack" );
			return;
		}

		var killerUnit = killerBackpack.GameObject.Components.GetInAncestorsOrSelf<UnitComponent>();
		if ( !killerUnit.IsValid() ) killerUnit = killerBackpack.GameObject.Components.GetInDescendantsOrSelf<UnitComponent>();
		if ( killerUnit.IsValid() && killerUnit.Team != TeamType.Player )
		{
			Log.Info( $"{Name}: no bounty paid because killer team is {killerUnit.Team}" );
			return;
		}

		var oldWallet = killerBackpack.Wallet;
		_bountyPaid = true;
		killerBackpack.AddMoney( Bounty );
		Log.Info( $"{Name}: paid ${Bounty} bounty to {killerBackpack.GameObject.Name}. Wallet {oldWallet} -> {killerBackpack.Wallet}" );
	}

	private static string PlayerLogName( GameObject player )
	{
		if ( !player.IsValid() ) return "unknown";
		var root = player.Root;
		if ( root.IsValid() && !string.IsNullOrWhiteSpace( root.Name ) ) return root.Name;
		return !string.IsNullOrWhiteSpace( player.Name ) ? player.Name : "unknown";
	}

	private Backpack FindKillerBackpack()
	{
		var backpack = _lastAttacker.Components.GetInDescendantsOrSelf<Backpack>();
		if ( backpack.IsValid() ) return backpack;

		backpack = _lastAttacker.Components.GetInAncestorsOrSelf<Backpack>();
		if ( backpack.IsValid() ) return backpack;

		var root = _lastAttacker.Root;
		if ( root.IsValid() && root != _lastAttacker )
		{
			backpack = root.Components.GetInDescendantsOrSelf<Backpack>();
			if ( backpack.IsValid() ) return backpack;
		}

		return null;
	}
}
