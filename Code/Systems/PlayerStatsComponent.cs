using Sandbox;

namespace Sandbox.Systems;

public enum PlayerStatType
{
	Health,
	Stamina,
	Punching,
	Ranged,
	Business,
}

public sealed class PlayerStatsComponent : Component
{
	public static readonly PlayerStatType[] LeveledStats =
	{
		PlayerStatType.Health,
		PlayerStatType.Stamina,
		PlayerStatType.Punching,
		PlayerStatType.Ranged,
		PlayerStatType.Business,
	};

	[Property]
	public StatsConfig Config { get; set; }

	[Property]
	public string ConfigPath { get; set; } = StatsConfig.DefaultPath;

	[Sync( SyncFlags.FromHost )] public float HealthXp { get; set; }
	[Sync( SyncFlags.FromHost )] public float StaminaXp { get; set; }
	[Sync( SyncFlags.FromHost )] public float PunchingXp { get; set; }
	[Sync( SyncFlags.FromHost )] public float RangedXp { get; set; }
	[Sync( SyncFlags.FromHost )] public float BusinessXp { get; set; }
	[Sync( SyncFlags.FromHost )] public int StatsVersion { get; set; }

	private UnitComponent _unit;
	private bool _hasLastPosition;
	private Vector3 _lastPosition;
	private float _xpFlushTimer;
	private float _pendingHealthXp;
	private float _pendingStaminaXp;
	private float _pendingPunchingXp;
	private float _pendingRangedXp;
	private float _pendingBusinessXp;

	[Hide] public StatsConfig StatConfig => Config ?? StatsConfig.Resolve( ConfigPath );
	[Hide] public int LevelCap => StatConfig.LevelCap;
	[Hide] public int HealthLevel => GetLevel( PlayerStatType.Health );
	[Hide] public int StaminaLevel => GetLevel( PlayerStatType.Stamina );
	[Hide] public int PunchingLevel => GetLevel( PlayerStatType.Punching );
	[Hide] public int RangedLevel => GetLevel( PlayerStatType.Ranged );
	[Hide] public int BusinessLevel => GetLevel( PlayerStatType.Business );
	[Hide] public float EffectiveMaxHealth => StatConfig.HealthMaxForLevel( HealthLevel );
	[Hide] public float EffectiveMaxStamina => StatConfig.StaminaMaxForLevel( StaminaLevel );
	[Hide] public float HealthRegenPerMinute => StatConfig.HealthRegenPerMinute;
	[Hide] public float MaxHydration => StatConfig.MaxHydration;
	[Hide] public float MaxNutrition => StatConfig.MaxNutrition;
	[Hide] public float HydrationDrainPerMinute => StatConfig.HydrationDrainPerMinute;
	[Hide] public float NutritionDrainPerMinute => StatConfig.NutritionDrainPerMinute;
	[Hide] public float ThirstyThreshold => StatConfig.ThirstyThreshold;
	[Hide] public float HungryThreshold => StatConfig.HungryThreshold;
	[Hide] public float ThirstyStaminaRegenMultiplier => StatConfig.ThirstyStaminaRegenMultiplier;
	[Hide] public float HungryHealthRegenMultiplier => StatConfig.HungryHealthRegenMultiplier;
	[Hide] public float StaminaRegenPerSecond => StatConfig.StaminaRegenPerSecond;
	[Hide] public float RunStaminaDrainPerSecond => StatConfig.RunStaminaDrainPerSecond;
	[Hide] public float SprintResumeStamina => StatConfig.SprintResumeStamina;
	[Hide] public float RangedSpreadDegrees => StatConfig.RangedSpreadForLevel( RangedLevel );
	[Hide] public float PunchStaminaCost => StatConfig.PunchStaminaCost;
	[Hide] public float PunchDamage => StatConfig.PunchBaseDamage * StatConfig.PunchDamageMultiplierForLevel( PunchingLevel );
	[Hide] public float BusinessIncomePerSecond => StatConfig.BusinessIncomePerSecondForLevel( BusinessLevel );

	[Hide]
	public float PlayerLevelExact
	{
		get
		{
			var total = 0f;
			foreach ( var stat in LeveledStats ) total += GetLevel( stat );
			return LeveledStats.Length > 0 ? total / LeveledStats.Length : 1f;
		}
	}

	[Hide]
	public int PlayerLevel => System.Math.Clamp( (int)System.MathF.Floor( PlayerLevelExact ), 1, LevelCap );

	[Hide]
	public float PlayerLevelProgressPercent
	{
		get
		{
			var total = 0f;
			foreach ( var stat in LeveledStats ) total += GetLevelProgress( stat );
			return LeveledStats.Length > 0 ? total / LeveledStats.Length : 0f;
		}
	}

	protected override void OnStart()
	{
		ResolveDependencies();
		RecordCurrentPosition();
		_xpFlushTimer = StatConfig.XpFlushInterval;
	}

	protected override void OnUpdate()
	{
		ResolveDependencies();
		if ( !Sandbox.Networking.IsHost ) return;

		TickHostProgression();
	}

	public int GetLevel( PlayerStatType stat )
	{
		return StatConfig.LevelForXp( GetXp( stat ) );
	}

	public float GetLevelProgress( PlayerStatType stat )
	{
		return StatConfig.LevelProgressForXp( GetXp( stat ) );
	}

	public float GetXp( PlayerStatType stat )
	{
		return stat switch
		{
			PlayerStatType.Health => HealthXp,
			PlayerStatType.Stamina => StaminaXp,
			PlayerStatType.Punching => PunchingXp,
			PlayerStatType.Ranged => RangedXp,
			PlayerStatType.Business => BusinessXp,
			_ => 0f,
		};
	}

	public float GetXpIntoLevel( PlayerStatType stat )
	{
		var level = GetLevel( stat );
		return float.Max( 0f, GetXp( stat ) - StatConfig.XpForLevel( level ) );
	}

	public float GetXpRequiredForCurrentLevel( PlayerStatType stat )
	{
		var level = GetLevel( stat );
		if ( level >= LevelCap ) return 0f;

		return StatConfig.XpForLevel( level + 1 ) - StatConfig.XpForLevel( level );
	}

	public void AwardRangedShot( bool hitDamageableTarget )
	{
		if ( !Sandbox.Networking.IsHost ) return;

		var amount = StatConfig.RangedShotXp;
		if ( hitDamageableTarget ) amount += StatConfig.RangedHitXp;
		QueueXp( PlayerStatType.Ranged, amount );
	}

	public void AwardHealthFoodXp( float amount )
	{
		if ( !Sandbox.Networking.IsHost ) return;

		QueueXp( PlayerStatType.Health, amount );
	}

	public void AwardPunchingXp( float amount )
	{
		if ( !Sandbox.Networking.IsHost ) return;

		QueueXp( PlayerStatType.Punching, amount );
	}

	public void AwardBusinessXp( float amount )
	{
		if ( !Sandbox.Networking.IsHost ) return;

		QueueXp( PlayerStatType.Business, amount );
	}

	private void TickHostProgression()
	{
		var config = StatConfig;
		var delta = Time.Delta;
		if ( delta <= 0f ) return;

		if ( !_unit.IsValid() || _unit.IsDead )
		{
			RecordCurrentPosition();
			FlushXpWhenDue( delta );
			return;
		}

		AwardMovementXp( config, delta );
		QueueXp( PlayerStatType.Health, config.HealthTimeXpPerSecond * delta );
		FlushXpWhenDue( delta );
	}

	private void AwardMovementXp( StatsConfig config, float delta )
	{
		var root = GameObject.Root;
		if ( !root.IsValid() ) return;

		var position = root.WorldPosition;
		if ( !_hasLastPosition )
		{
			RecordCurrentPosition();
			return;
		}

		var moved = position - _lastPosition;
		moved.z = 0f;
		_lastPosition = position;

		var speed = moved.Length / float.Max( delta, 0.001f );
		if ( speed < config.MovementSpeedThreshold ) return;

		var runMultiplier = _unit.IsRunStaminaDrainActive ? config.StaminaRunXpMultiplier : 1f;
		QueueXp( PlayerStatType.Stamina, config.StaminaWalkXpPerSecond * runMultiplier * delta );
	}

	private void QueueXp( PlayerStatType stat, float amount )
	{
		if ( amount <= 0f ) return;

		amount *= float.Max( 0f, StatConfig.GlobalXpMultiplier );
		if ( amount <= 0f ) return;

		switch ( stat )
		{
			case PlayerStatType.Health:
				_pendingHealthXp += amount;
				break;
			case PlayerStatType.Stamina:
				_pendingStaminaXp += amount;
				break;
			case PlayerStatType.Punching:
				_pendingPunchingXp += amount;
				break;
			case PlayerStatType.Ranged:
				_pendingRangedXp += amount;
				break;
			case PlayerStatType.Business:
				_pendingBusinessXp += amount;
				break;
		}
	}

	private void FlushXpWhenDue( float delta )
	{
		_xpFlushTimer -= delta;
		if ( _xpFlushTimer > 0f ) return;

		FlushPendingXp();
		_xpFlushTimer = float.Max( 0.05f, StatConfig.XpFlushInterval );
	}

	private void FlushPendingXp()
	{
		var changed = false;
		changed |= FlushPendingStatXp( ref _pendingHealthXp, HealthXp, x => HealthXp = x );
		changed |= FlushPendingStatXp( ref _pendingStaminaXp, StaminaXp, x => StaminaXp = x );
		changed |= FlushPendingStatXp( ref _pendingPunchingXp, PunchingXp, x => PunchingXp = x );
		changed |= FlushPendingStatXp( ref _pendingRangedXp, RangedXp, x => RangedXp = x );
		changed |= FlushPendingStatXp( ref _pendingBusinessXp, BusinessXp, x => BusinessXp = x );

		if ( changed ) StatsVersion++;
	}

	private bool FlushPendingStatXp( ref float pending, float current, System.Action<float> setter )
	{
		if ( pending <= 0f ) return false;

		var next = float.Clamp( current + pending, 0f, StatConfig.MaxXp );
		pending = 0f;

		if ( System.MathF.Abs( next - current ) <= 0.001f ) return false;

		setter( next );
		return true;
	}

	private void ResolveDependencies()
	{
		if ( !_unit.IsValid() )
		{
			_unit = GameObject.Root.Components.GetInDescendantsOrSelf<UnitComponent>();
		}
	}

	private void RecordCurrentPosition()
	{
		var root = GameObject.Root;
		if ( !root.IsValid() ) return;

		_lastPosition = root.WorldPosition;
		_hasLastPosition = true;
	}
}
