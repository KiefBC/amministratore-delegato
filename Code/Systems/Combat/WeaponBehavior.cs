using Sandbox;
using Sandbox.Citizen;

/// <summary>
/// Firearm behavior — aim, fire, holster. Reads the currently equipped weapon from
/// <see cref="Inventory"/> on the same GameObject; stats (Damage/Range/HoldType/offsets)
/// are copied in by <see cref="Inventory.Equip"/> from the world <see cref="WeaponPickup"/>.
/// </summary>
public sealed class WeaponBehavior : Component
{
	[Property] public SkinnedModelRenderer BodyRenderer { get; set; }
	[Property] public CitizenAnimationHelper AnimHelper { get; set; }
	[Property] public Inventory Inventory { get; set; }

	[Property]
	public CitizenAnimationHelper.HoldTypes HoldType { get; set; }
		= CitizenAnimationHelper.HoldTypes.Pistol;

	[Property]
	public CitizenAnimationHelper.Hand Handedness { get; set; }
		= CitizenAnimationHelper.Hand.Right;

	/// <summary>
	/// Damage dealt per shot.
	/// </summary>
	[Property]
	[Range( 1f, 200f )]
	[Step( 1f )]
	public float Damage { get; set; } = 25f;

	/// <summary>
	/// Maximum trace distance for a shot.
	/// </summary>
	[Property]
	[Range( 100f, 10000f )]
	[Step( 100f )]
	public float Range { get; set; } = 5000f;

	[Property] public Vector3 WeaponOffset { get; set; } = Vector3.Zero;
	[Property] public Angles WeaponAngleOffset { get; set; } = Angles.Zero;
	[Property] public Vector3 WeaponScale { get; set; } = Vector3.One;

	private GameObject Weapon => Inventory?.Equipped;
	private bool _holstered;

	protected override void OnStart()
	{
		Inventory ??= Components.Get<Inventory>();
		BodyRenderer ??= Components.Get<SkinnedModelRenderer>();
		AnimHelper ??= Components.Get<CitizenAnimationHelper>();
	}

	protected override void OnUpdate()
	{
		if ( AnimHelper is null ) return;

		ApplyWeaponOffset();

		if ( Input.Pressed( "Slot1" ) && Weapon.IsValid() )
		{
			_holstered = !_holstered;
			Weapon.Enabled = !_holstered;
		}

		var hasWeapon = Weapon.IsValid();
		var aiming = Input.Down( "Attack2" ) && !_holstered && hasWeapon;

		if ( aiming )
		{
			AnimHelper.HoldType = HoldType;
			AnimHelper.Handedness = Handedness;
			AnimHelper.IsWeaponLowered = false;
		}
		else
		{
			AnimHelper.HoldType = CitizenAnimationHelper.HoldTypes.None;
		}

		if ( Input.Pressed( "Attack1" ) && !_holstered && hasWeapon )
		{
			Fire();
		}
	}

	private void ApplyWeaponOffset()
	{
		if ( !Weapon.IsValid() ) return;
		Weapon.LocalPosition = WeaponOffset;
		Weapon.LocalRotation = Rotation.From( WeaponAngleOffset );
		Weapon.LocalScale = WeaponScale;
	}

	private void Fire()
	{
		var camera = Scene.Camera;
		if ( !camera.IsValid() ) return;

		var startPos = camera.WorldPosition;
		var endPos = startPos + camera.WorldRotation.Forward * Range;

		var trace = Scene.Trace.Ray( startPos, endPos )
			.IgnoreGameObjectHierarchy( GameObject.Root )
			.Run();

		if ( !trace.Hit )
		{
			Log.Info( "Shot - missed" );
			SpawnDebugMarker( endPos, Color.Yellow );
			return;
		}

		Log.Info( $"Shot hit {trace.GameObject?.Name}" );
		SpawnDebugMarker( trace.HitPosition, Color.Red );

		var hit = trace.GameObject?.Components.GetInAncestorsOrSelf<Component.IDamageable>();
		if ( hit is null ) return;

		var info = new DamageInfo
		{
			Damage = Damage,
			Position = trace.HitPosition,
			Origin = startPos,
			Attacker = GameObject.Root,
			Weapon = Weapon,
		};

		CombatSystem.Current.DealDamage( hit, info );
	}

	private void SpawnDebugMarker( Vector3 position, Color color )
	{
		var go = new GameObject();
		go.Name = "ShotDebug";
		go.WorldPosition = position;
		go.WorldScale = Vector3.One * 0.1f;

		var renderer = go.Components.Create<ModelRenderer>();
		renderer.Model = Model.Load( "models/dev/sphere.vmdl" );
		renderer.Tint = color;

		var marker = go.Components.Create<DebugMarker>();
		marker.Lifetime = 2f;
	}
}
