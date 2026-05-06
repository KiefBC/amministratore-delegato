using Sandbox;
using Sandbox.Citizen;
using System.Collections.Generic;

[AssetType( Name = "Item Definition", Extension = "item", Category = "Amministratore" )]
public partial class ItemDefinition : GameResource
{
	public const string GlockPath = "items/glock.item";
	public const string MoneyPath = "items/money.item";

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

	[ResourceType( "vmdl" )]
	public string EquippedModel { get; set; } = "";

	public CitizenAnimationHelper.HoldTypes HoldType { get; set; } = CitizenAnimationHelper.HoldTypes.Pistol;
	public CitizenAnimationHelper.Hand Handedness { get; set; } = CitizenAnimationHelper.Hand.Right;
	public float Damage { get; set; } = 25f;
	public float Range { get; set; } = 3000f;
	public int MagazineSize { get; set; } = 15;
	public float ReloadDuration { get; set; } = 1.5f;
	public float FireInterval { get; set; } = 0.2f;
	public Vector3 WeaponOffset { get; set; } = Vector3.Zero;
	public Angles WeaponAngleOffset { get; set; } = Angles.Zero;
	public Vector3 WeaponScale { get; set; } = Vector3.One;

	public bool IsWeapon => Kind == ItemKind.Weapon;
	public bool IsCurrency => Kind == ItemKind.Currency;
	public string ModelPath => string.IsNullOrWhiteSpace( EquippedModel ) ? WorldModel : EquippedModel;

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
				EquippedModel = "models workshop/glock g20/glockg20.vmdl",
				HoldType = CitizenAnimationHelper.HoldTypes.Pistol,
				Handedness = CitizenAnimationHelper.Hand.Right,
				Damage = 25f,
				Range = 3000f,
				MagazineSize = 15,
				ReloadDuration = 1.5f,
				FireInterval = 0.2f,
				WeaponOffset = new Vector3( 3.01600003f, -0.864000022f, 2.12800002f ),
				WeaponAngleOffset = new Angles( -11.5679998f, -8.44799995f, 0f ),
				WeaponScale = Vector3.One * 0.680000007f,
			},
			MoneyPath => new ItemDefinition
			{
				DisplayName = "Money",
				Kind = ItemKind.Currency,
				Value = 1,
				Weight = 0,
				MaxStack = int.MaxValue,
			},
			_ => null,
		};
	}
}
