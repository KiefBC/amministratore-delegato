using Sandbox;

namespace Sandbox.Systems;

[AssetType( Name = "Stats Config", Extension = "statcfg", Category = "Amministratore" )]
public partial class StatsConfig : GameResource
{
	public const string DefaultPath = "stats/default.statcfg";

	private static StatsConfig _fallback;

	[Property, Range( 1, 99 ), Step( 1 )]
	public int LevelCap { get; set; } = 99;

	[Property, Range( 0f, 1000f ), Step( 0.1f )]
	public float GlobalXpMultiplier { get; set; } = 1f;

	[Property, Range( 1f, 1000f ), Step( 1f )]
	public float XpBase { get; set; } = 25f;

	[Property, Range( 1f, 2f ), Step( 0.005f )]
	public float XpGrowth { get; set; } = 1.115f;

	[Property, Range( 0.1f, 4f ), Step( 0.05f )]
	public float XpPower { get; set; } = 1.25f;

	[Property, Range( 1f, 100f ), Step( 1f )]
	public float HealthStartMax { get; set; } = 1f;

	[Property, Range( 1f, 1000f ), Step( 1f )]
	public float HealthCap { get; set; } = 250f;

	[Property, Range( 1f, 100f ), Step( 1f )]
	public float StaminaStartMax { get; set; } = 5f;

	[Property, Range( 1f, 1000f ), Step( 1f )]
	public float StaminaCap { get; set; } = 200f;

	[Property, Range( 0f, 100f ), Step( 0.1f )]
	public float StaminaWalkXpPerSecond { get; set; } = 0.25f;

	[Property, Range( 1f, 10f ), Step( 0.1f )]
	public float StaminaRunXpMultiplier { get; set; } = 1.5f;

	[Property, Range( 0f, 200f ), Step( 1f )]
	public float MovementSpeedThreshold { get; set; } = 5f;

	[Property, Range( 0f, 100f ), Step( 0.01f )]
	public float HealthTimeXpPerSecond { get; set; } = 0.03f;

	[Property, Range( 0f, 100f ), Step( 1f )]
	public float HealthRegenPerMinute { get; set; } = 1f;

	[Property, Range( 0f, 1000f ), Step( 1f )]
	public float MaxHydration { get; set; } = 100f;

	[Property, Range( 0f, 1000f ), Step( 1f )]
	public float MaxNutrition { get; set; } = 100f;

	[Property, Range( 0f, 100f ), Step( 0.1f )]
	public float HydrationDrainPerMinute { get; set; } = 1f;

	[Property, Range( 0f, 100f ), Step( 0.1f )]
	public float NutritionDrainPerMinute { get; set; } = 0.5f;

	[Property, Range( 0f, 1000f ), Step( 1f )]
	public float ThirstyThreshold { get; set; } = 0f;

	[Property, Range( 0f, 1000f ), Step( 1f )]
	public float HungryThreshold { get; set; } = 0f;

	[Property, Range( 0f, 1f ), Step( 0.05f )]
	public float ThirstyStaminaRegenMultiplier { get; set; } = 0.5f;

	[Property, Range( 0f, 1f ), Step( 0.05f )]
	public float HungryHealthRegenMultiplier { get; set; } = 0f;

	[Property, Range( 0f, 100f ), Step( 0.1f )]
	public float StaminaRegenPerSecond { get; set; } = 1f;

	[Property, Range( 0f, 100f ), Step( 0.1f )]
	public float RunStaminaDrainPerSecond { get; set; } = 1f;

	[Property, Range( 0f, 100f ), Step( 0.1f )]
	public float SprintResumeStamina { get; set; } = 1f;

	[Property, Range( 0f, 100f ), Step( 0.01f )]
	public float RangedShotXp { get; set; } = 0.05f;

	[Property, Range( 0f, 100f ), Step( 0.01f )]
	public float RangedHitXp { get; set; } = 0.5f;

	[Property, Range( 0f, 45f ), Step( 0.25f )]
	public float RangedMaxSpreadDegrees { get; set; } = 12f;

	[Property, Range( 0f, 45f ), Step( 0.25f )]
	public float RangedMinSpreadDegrees { get; set; } = 0.25f;

	[Property, Range( 0f, 100f ), Step( 0.1f )]
	public float PunchStaminaCost { get; set; } = 1f;

	[Property, Range( 0f, 100f ), Step( 0.1f )]
	public float PunchBaseDamage { get; set; } = 1f;

	[Property, Range( 1f, 20f ), Step( 0.1f )]
	public float PunchMaxDamageMultiplier { get; set; } = 3f;

	[Property, Range( 0f, 1000f ), Step( 0.1f )]
	public float BusinessBaseIncomePerSecond { get; set; } = 0.1f;

	[Property, Range( 0.05f, 5f ), Step( 0.05f )]
	public float XpFlushInterval { get; set; } = 0.25f;

	[Hide]
	public float MaxXp => XpForLevel( LevelCap );

	public static StatsConfig Resolve( string path )
	{
		path = NormalizePath( path );
		if ( string.IsNullOrWhiteSpace( path ) ) path = DefaultPath;

		var resource = ResourceLibrary.Get<StatsConfig>( path );
		if ( resource is not null ) return resource;

		return _fallback ??= new StatsConfig();
	}

	public int ClampLevel( int level )
	{
		return System.Math.Clamp( level, 1, int.Max( 1, LevelCap ) );
	}

	public int LevelForXp( float xp )
	{
		var cap = int.Max( 1, LevelCap );
		if ( xp <= 0f ) return 1;

		for ( var level = 1; level < cap; level++ )
		{
			if ( xp < XpForLevel( level + 1 ) ) return level;
		}

		return cap;
	}

	public float LevelProgressForXp( float xp )
	{
		var level = LevelForXp( xp );
		if ( level >= LevelCap ) return 1f;

		var current = XpForLevel( level );
		var next = XpForLevel( level + 1 );
		if ( next <= current ) return 1f;

		return float.Clamp( (xp - current) / (next - current), 0f, 1f );
	}

	public float XpForLevel( int level )
	{
		level = ClampLevel( level );
		if ( level <= 1 ) return 0f;

		var total = 0f;
		for ( var current = 1; current < level; current++ )
		{
			total += XpRequiredForNextLevel( current );
		}

		return total;
	}

	public float XpRequiredForNextLevel( int currentLevel )
	{
		currentLevel = ClampLevel( currentLevel );
		return XpBase
			* System.MathF.Pow( currentLevel, XpPower )
			* System.MathF.Pow( XpGrowth, currentLevel - 1 );
	}

	public float HealthMaxForLevel( int level )
	{
		return PoolForLevel( HealthStartMax, HealthCap, level );
	}

	public float StaminaMaxForLevel( int level )
	{
		return PoolForLevel( StaminaStartMax, StaminaCap, level );
	}

	public float RangedSpreadForLevel( int level )
	{
		return PoolForLevel( RangedMaxSpreadDegrees, RangedMinSpreadDegrees, level );
	}

	public float PunchDamageMultiplierForLevel( int level )
	{
		return PoolForLevel( 1f, PunchMaxDamageMultiplier, level );
	}

	public float BusinessIncomePerSecondForLevel( int level )
	{
		return BusinessBaseIncomePerSecond * level;
	}

	private float PoolForLevel( float start, float cap, int level )
	{
		var levelCap = int.Max( 1, LevelCap );
		if ( levelCap <= 1 ) return cap;

		var t = (float)(ClampLevel( level ) - 1) / (levelCap - 1);
		return start + ((cap - start) * t);
	}

	private static string NormalizePath( string path )
	{
		return string.IsNullOrWhiteSpace( path )
			? ""
			: path.Replace( '\\', '/' ).Trim().ToLowerInvariant();
	}
}
