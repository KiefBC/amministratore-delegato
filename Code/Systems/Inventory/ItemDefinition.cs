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

	[HideIf( nameof( IsNotWeaponKind ), true )]
	public WeaponStats Weapon { get; set; }

	[Hide]
	public bool IsWeapon => Kind == ItemKind.Weapon && Weapon is not null;
	[Hide]
	public bool IsCurrency => Kind == ItemKind.Currency;
	[Hide]
	public string ModelPath => !string.IsNullOrWhiteSpace( Weapon?.EquippedModel ) ? Weapon.EquippedModel : WorldModel;
	[Hide]
	public bool IsNotWeaponKind => Kind != ItemKind.Weapon;

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
			_ => null,
		};
	}
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
