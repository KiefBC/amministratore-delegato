using Sandbox;

public sealed class UnitComponent : Component, Component.IDamageable
{
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
	private bool _deathApplied;
	private float _pendingHealthRegen;

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

		if ( !Networking.IsHost )
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

		if ( !Networking.IsHost )
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

		if ( !Networking.IsHost )
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

		if ( !Networking.IsHost )
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
		if ( !Networking.IsHost ) return;
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
			IsDead = true;
			TryPayBounty();
		}
	}

	protected override void OnStart()
	{
		ResolvePlayerStats();

		if ( Networking.IsHost )
		{
			Health = EffectiveMaxHealth;
			Stamina = EffectiveMaxStamina;
			Armor = MaxArmor;
			Hydration = EffectiveMaxHydration;
			Nutrition = EffectiveMaxNutrition;
			UpdateNeedsState();
			IsDead = false;
		}

		_lastAppliedMaxHealth = EffectiveMaxHealth;
		_lastAppliedMaxStamina = EffectiveMaxStamina;
		_lastAppliedStaminaLevel = ResolvePlayerStats()?.StaminaLevel ?? 1;
		_lastHealth = Health;
		_deathApplied = false;
	}

	protected override void OnUpdate()
	{
		if ( Networking.IsHost && !IsDead )
		{
			TickNeeds();
			ApplyStatDerivedPools();
			RegenerateHealth();
			UpdateSprintLock();

			_isRunStaminaDrainActive = false;
			if ( _wantsRunStaminaDrain && !_sprintLocked && Stamina > 0f ) DrainRunStamina();
			else RegenerateStamina();
		}

		if ( IsDead && !_deathApplied )
		{
			ApplyDeathState();
		}

		if ( Health == _lastHealth ) return;

		var difference = Health - _lastHealth;
		Log.Info( $"{Name} health changed by {difference:F0}, new health is {Health:F0}" );
		_lastHealth = Health;
	}

	public bool TrySpendStamina( float amount )
	{
		if ( !Networking.IsHost ) return false;
		if ( amount <= 0f ) return true;
		if ( Stamina < amount ) return false;

		Stamina = float.Clamp( Stamina - amount, 0f, EffectiveMaxStamina );
		if ( Stamina <= 0f ) _sprintLocked = true;
		return true;
	}

	public void RestoreStamina( float amount )
	{
		if ( !Networking.IsHost ) return;
		if ( amount <= 0f ) return;

		Stamina = float.Clamp( Stamina + amount, 0f, EffectiveMaxStamina );
	}

	public void SetArmor( float amount )
	{
		if ( !Networking.IsHost ) return;

		Armor = float.Clamp( amount, 0f, MaxArmor );
	}

	public void RestoreHydration( float amount )
	{
		if ( !Networking.IsHost ) return;
		if ( amount <= 0f ) return;

		Hydration = float.Clamp( Hydration + amount, 0f, EffectiveMaxHydration );
		UpdateNeedsState();
	}

	public void RestoreNutrition( float amount )
	{
		if ( !Networking.IsHost ) return;
		if ( amount <= 0f ) return;

		Nutrition = float.Clamp( Nutrition + amount, 0f, EffectiveMaxNutrition );
		UpdateNeedsState();
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

	private UnitComponent DebugTargetUnit()
	{
		if ( GameObject.Enabled && Sandbox.LocalPlayer.Owns( GameObject ) ) return this;

		var localUnit = Sandbox.LocalPlayer.Component<UnitComponent>( Scene );
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
		if ( !Networking.IsHost ) return;

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

	private float EffectiveHealthRegenPerMinute => (ResolvePlayerStats()?.HealthRegenPerMinute ?? HealthRegenPerMinute) * (IsHungry ? EffectiveHungryHealthRegenMultiplier : 1f);
	private float EffectiveStaminaRegenRate => (ResolvePlayerStats()?.StaminaRegenPerSecond ?? StaminaRegenRate) * (IsThirsty ? EffectiveThirstyStaminaRegenMultiplier : 1f);
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

	private void ApplyDeathState()
	{
		_deathApplied = true;
		Log.Info( $"{Name} died" );

		if ( Physics.IsValid() )
		{
			Physics.MotionEnabled = true;
		}
		else
		{
			Log.Warning( $"{Name}: Physics property is not assigned — cannot ragdoll on death" );
		}

		if ( Patrol.IsValid() )
		{
			Patrol.Enabled = false;
		}

		TryPayBounty();
	}

	private void TryPayBounty()
	{
		if ( !Networking.IsHost ) return;
		if ( _bountyPaid ) return;
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
