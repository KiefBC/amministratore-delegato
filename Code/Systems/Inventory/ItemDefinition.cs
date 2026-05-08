using Sandbox;
using Sandbox.Citizen;
using System.Collections.Generic;

[AssetType( Name = "Item Definition", Extension = "item", Category = "Amministratore" )]
public partial class ItemDefinition : GameResource
{
	public const string GlockPath = "items/glock.item";
	public const string MoneyPath = "items/money.item";
	public const string WaterPath = "items/water.item";
	public const string BeerPath = "items/beer.item";
	public const string FastFoodPath = "items/fast_food.item";
	public const string RestaurantMealPath = "items/restaurant_meal.item";

	private static readonly Dictionary<string, ItemDefinition> Fallbacks = new();

	public string DisplayName { get; set; } = "Item";
	public ItemKind Kind { get; set; } = ItemKind.Generic;
	public string Icon { get; set; } = "";
	public Rarity Rarity { get; set; } = Rarity.Common;
	public int Value { get; set; }
	public int Weight { get; set; }
	public int MaxStack { get; set; } = 1;

	[ResourceType( "vmdl" )]
	public string WorldModel { get; set; } = "";

	[HideIf( nameof( IsNotWeaponKind ), true )]
	public WeaponStats Weapon { get; set; }
	[HideIf( nameof( IsNotConsumableKind ), true )]
	public ConsumableStats Consumable { get; set; }

	[Hide]
	public bool IsWeapon => Kind == ItemKind.Weapon && Weapon is not null;
	[Hide]
	public bool IsCurrency => Kind == ItemKind.Currency;
	[Hide]
	public bool IsConsumable => Kind == ItemKind.Consumable && Consumable is not null;
	[Hide]
	public string ModelPath => !string.IsNullOrWhiteSpace( Weapon?.EquippedModel ) ? Weapon.EquippedModel : WorldModel;
	[Hide]
	public bool IsNotWeaponKind => Kind != ItemKind.Weapon;
	[Hide]
	public bool IsNotConsumableKind => Kind != ItemKind.Consumable;

	public static string PathFor( ItemDefinition definition )
	{
		return NormalizePath( definition?.ResourcePath );
	}

	public static ItemDefinition Resolve( string path )
	{
		path = NormalizePath( path );
		if ( string.IsNullOrWhiteSpace( path ) ) return null;

		var resource = ResourceLibrary.Get<ItemDefinition>( path );
		if ( resource is not null ) return resource;

		if ( Fallbacks.TryGetValue( path, out var fallback ) ) return fallback;

		fallback = CreateFallback( path );
		if ( fallback is not null )
		{
			Fallbacks[path] = fallback;
		}

		return fallback;
	}

	public static Model LoadModel( string path )
	{
		if ( string.IsNullOrWhiteSpace( path ) ) return null;
		return Model.Load( path );
	}

	private static string NormalizePath( string path )
	{
		return string.IsNullOrWhiteSpace( path )
			? ""
			: path.Replace( '\\', '/' ).Trim().ToLowerInvariant();
	}

	private static ItemDefinition CreateFallback( string path )
	{
		return path switch
		{
			GlockPath => new ItemDefinition
			{
				DisplayName = "Glock",
				Kind = ItemKind.Weapon,
				Rarity = Rarity.Common,
				Value = 200,
				Weight = 1,
				MaxStack = 1,
				WorldModel = "models workshop/glock g20/glockg20.vmdl",
				Weapon = new WeaponStats
				{
					EquippedModel = "models workshop/glock g20/glockg20.vmdl",
					HandBone = "hold_R",
					HoldType = CitizenAnimationHelper.HoldTypes.Pistol,
					Handedness = CitizenAnimationHelper.Hand.Right,
					Damage = 25f,
					Range = 3000f,
					ClipSize = 15,
					ReloadDuration = 1.5f,
					FireRate = 5f,
					TraceSize = 6f,
					Offset = new Vector3( 3.01600003f, -0.864000022f, 2.12800002f ),
					AngleOffset = new Angles( -11.5679998f, -8.44799995f, 0f ),
					Scale = Vector3.One * 0.680000007f,
				},
			},
			MoneyPath => new ItemDefinition
			{
				DisplayName = "Money",
				Kind = ItemKind.Currency,
				Value = 1,
				Weight = 0,
				MaxStack = int.MaxValue,
			},
			WaterPath => new ItemDefinition
			{
				DisplayName = "Water",
				Kind = ItemKind.Consumable,
				Value = 2,
				Weight = 1,
				MaxStack = 10,
				Consumable = new ConsumableStats
				{
					Category = ConsumableCategory.Drink,
					Tier = ConsumableTier.Basic,
					Hydration = 30f,
					StaminaRegenPerSecond = 0.5f,
					EffectDuration = 30f,
				},
			},
			BeerPath => new ItemDefinition
			{
				DisplayName = "Beer",
				Kind = ItemKind.Consumable,
				Value = 6,
				Weight = 1,
				MaxStack = 6,
				Consumable = new ConsumableStats
				{
					Category = ConsumableCategory.Drink,
					Tier = ConsumableTier.Basic,
					Stamina = 3f,
					Hydration = -15f,
					StaminaRegenPerSecond = -0.5f,
					EffectDuration = 30f,
				},
			},
			FastFoodPath => new ItemDefinition
			{
				DisplayName = "Fast Food",
				Kind = ItemKind.Consumable,
				Value = 5,
				Weight = 1,
				MaxStack = 5,
				Consumable = new ConsumableStats
				{
					Category = ConsumableCategory.Food,
					Tier = ConsumableTier.Cheap,
					Health = 1f,
					Nutrition = 25f,
					HealthXp = 0.1f,
				},
			},
			RestaurantMealPath => new ItemDefinition
			{
				DisplayName = "Restaurant Meal",
				Kind = ItemKind.Consumable,
				Value = 25,
				Weight = 1,
				MaxStack = 3,
				Consumable = new ConsumableStats
				{
					Category = ConsumableCategory.Food,
					Tier = ConsumableTier.Quality,
					Health = 5f,
					Nutrition = 60f,
					HealthRegenPerSecond = 0.2f,
					EffectDuration = 30f,
					HealthXp = 1f,
				},
			},
			_ => null,
		};
	}
}

public enum ConsumableCategory
{
	[Description( "Food item. Usually restores nutrition and health." )]
	Food,
	[Description( "Drink item. Usually restores hydration or changes stamina behavior." )]
	Drink,
}

public enum ConsumableTier
{
	[Description( "Low-cost, low-effect consumable." )]
	Cheap,
	[Description( "Baseline consumable quality." )]
	Basic,
	[Description( "Better consumable with stronger flat or timed effects." )]
	Quality,
	[Description( "High-end consumable reserved for expensive or rare effects." )]
	Premium,
}

public sealed class ConsumableStats
{
	[Description( "Broad consumable type for tuning and future UI filtering. Food and Drink do not change behavior by themselves." )]
	public ConsumableCategory Category { get; set; } = ConsumableCategory.Food;
	[Description( "Quality/price tier label for balancing. Tier does not change behavior by itself." )]
	public ConsumableTier Tier { get; set; } = ConsumableTier.Basic;

	[Range( -500f, 500f ), Step( 0.5f )]
	[Description( "Flat current health change on use. Positive heals, negative damages, clamped to the player's effective health range." )]
	public float Health { get; set; }
	[Range( -500f, 500f ), Step( 0.5f )]
	[Description( "Flat current stamina change on use. Positive restores stamina, negative drains it, clamped to the player's effective stamina range." )]
	public float Stamina { get; set; }
	[Range( -500f, 500f ), Step( 0.5f )]
	[Description( "Flat hydration change on use. Positive hydrates, negative dehydrates, clamped to the player's hydration range." )]
	public float Hydration { get; set; }
	[Range( -500f, 500f ), Step( 0.5f )]
	[Description( "Flat nutrition change on use. Positive fills hunger, negative reduces nutrition, clamped to the player's nutrition range." )]
	public float Nutrition { get; set; }

	[Range( -100f, 100f ), Step( 0.05f )]
	[Description( "Timed additive health regen modifier in health per second. Replaces the previous health regen consumable modifier while active." )]
	public float HealthRegenPerSecond { get; set; }
	[Range( -100f, 100f ), Step( 0.05f )]
	[Description( "Timed additive stamina regen modifier in stamina per second. Replaces the previous stamina regen consumable modifier while active." )]
	public float StaminaRegenPerSecond { get; set; }
	[Range( 0f, 600f ), Step( 1f )]
	[Description( "Duration in seconds for health/stamina regen modifiers. Set to 0 for only flat instant effects." )]
	public float EffectDuration { get; set; }
	[Range( 0f, 1000f ), Step( 0.1f )]
	[Description( "Optional health stat XP awarded by the host after this consumable is successfully used." )]
	public float HealthXp { get; set; }
}

public sealed class WeaponStats
{
	[ResourceType( "vmdl" )]
	public string EquippedModel { get; set; } = "";

	/// <summary>Bone to parent this weapon model to before applying Offset, AngleOffset, and Scale.</summary>
	public string HandBone { get; set; } = "hold_R";
	public CitizenAnimationHelper.HoldTypes HoldType { get; set; } = CitizenAnimationHelper.HoldTypes.Pistol;
	public CitizenAnimationHelper.Hand Handedness { get; set; } = CitizenAnimationHelper.Hand.Right;
	[Range( 1f, 500f ), Step( 1f )]
	public float Damage { get; set; } = 25f;
	[Range( 100f, 10000f ), Step( 100f )]
	public float Range { get; set; } = 3000f;
	[Range( 1, 200 ), Step( 1 )]
	public int ClipSize { get; set; } = 15;
	[Range( 0.1f, 10f ), Step( 0.1f )]
	public float ReloadDuration { get; set; } = 1.5f;
	[Range( 0.1f, 20f ), Step( 0.1f )]
	public float FireRate { get; set; } = 5f;
	[Range( 0f, 32f ), Step( 0.5f )]
	public float TraceSize { get; set; } = 6f;
	/// <summary>Local position offset from HandBone for fine tuning weapon grip.</summary>
	public Vector3 Offset { get; set; } = Vector3.Zero;
	/// <summary>Local rotation offset from HandBone for fine tuning weapon grip.</summary>
	public Angles AngleOffset { get; set; } = Angles.Zero;
	/// <summary>Local scale applied after the weapon is parented to HandBone.</summary>
	public Vector3 Scale { get; set; } = Vector3.One;

	[Hide]
	public float FireInterval => FireRate <= 0f ? float.MaxValue : 1f / FireRate;
}
