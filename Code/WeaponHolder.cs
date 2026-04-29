using Sandbox;
using Sandbox.Citizen;

public sealed class WeaponHolder : Component
{
	[Property] public SkinnedModelRenderer BodyRenderer { get; set; }
	[Property] public GameObject Weapon { get; set; }
	[Property] private string HandBone { get; set; } = "hold_R";
	[Property] public CitizenAnimationHelper AnimHelper { get; set; }
	[Property] public ChairInteraction Sit { get; set; }

	/// <summary>
	/// Local-space position offset of the held weapon relative to the hand bone.
	/// Tune live during Play to find the right grip position.
	/// </summary>
	[Property]
	public Vector3 WeaponOffset { get; set; } = Vector3.Zero;

	/// <summary>
	/// Local-space rotation offset (pitch/yaw/roll, degrees) of the held weapon relative to the hand bone.
	/// Tune live during Play to find the right grip rotation.
	/// </summary>
	[Property]
	public Angles WeaponAngleOffset { get; set; } = Angles.Zero;

	/// <summary>
	/// Local-space scale of the held weapon. Tune live during Play.
	/// </summary>
	[Property]
	public Vector3 WeaponScale { get; set; } = Vector3.One;

	[Property]
	public CitizenAnimationHelper.HoldTypes HoldType { get; set; }
		= CitizenAnimationHelper.HoldTypes.Pistol;

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

	/// <summary>
	/// How close the player must be to a WeaponPickup to grab it on Use.
	/// </summary>
	[Property]
	[Range( 10f, 500f )]
	[Step( 10f )]
	public float PickupRange { get; set; } = 100f;

	/// <summary>
	/// The closest WeaponPickup currently within PickupRange, or null. Updated each frame.
	/// </summary>
	public WeaponPickup NearbyPickup { get; private set; }

	private bool _holstered;

	protected override void OnStart()
	{
		var boneObject = BodyRenderer.GetBoneObject( HandBone );
		if ( boneObject is not null && Weapon is not null )
		{
			Weapon.SetParent( boneObject, false );
			ApplyWeaponOffset();
		}
	}

	private void ApplyWeaponOffset()
	{
		if ( !Weapon.IsValid() ) return;
		Weapon.LocalPosition = WeaponOffset;
		Weapon.LocalRotation = Rotation.From( WeaponAngleOffset );
		Weapon.LocalScale = WeaponScale;
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

		NearbyPickup = FindNearestPickup();

		if ( Input.Pressed( "Use" ) && NearbyPickup is not null && Sit?.IsSitting != true )
		{
			AcquireWeapon( NearbyPickup );
			NearbyPickup = null;
		}
	}

	private WeaponPickup FindNearestPickup()
	{
		WeaponPickup nearest = null;
		float nearestDistance = PickupRange;

		foreach ( var pickup in Scene.GetAllComponents<WeaponPickup>() )
		{
			if ( !pickup.IsValid() ) continue;
			if ( pickup.GameObject.Root == GameObject.Root ) continue;

			var dist = pickup.WorldPosition.Distance( WorldPosition );
			if ( dist < nearestDistance )
			{
				nearest = pickup;
				nearestDistance = dist;
			}
		}

		return nearest;
	}

	private void AcquireWeapon( WeaponPickup pickup )
	{
		Log.Info( $"Picked up {pickup.GameObject.Name}" );

		if ( Weapon.IsValid() )
		{
			Weapon.Destroy();
		}

		Weapon = pickup.GameObject;
		HoldType = pickup.HoldType;
		Damage = pickup.Damage;
		Range = pickup.Range;
		WeaponOffset = pickup.WeaponOffset;
		WeaponAngleOffset = pickup.WeaponAngleOffset;
		WeaponScale = pickup.WeaponScale;

		var boneObject = BodyRenderer.GetBoneObject( HandBone );
		if ( boneObject is not null )
		{
			Weapon.SetParent( boneObject, false );
			ApplyWeaponOffset();
		}

		foreach ( var collider in Weapon.Components.GetAll<Collider>() )
		{
			collider.Enabled = false;
		}

		_holstered = false;

		pickup.Destroy();
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
