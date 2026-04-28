using Sandbox;
using Sandbox.Citizen;

public sealed class WeaponHolder : Component
{
	[Property] public SkinnedModelRenderer BodyRenderer { get; set; }
	[Property] public GameObject Weapon { get; set; }
	[Property] private string HandBone { get; set; } = "hold_R";
	[Property] public CitizenAnimationHelper AnimHelper { get; set; }

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

	protected override void OnStart()
	{
		Log.Info( "WeaponHolder OnStart fired" );

		var boneObject = BodyRenderer.GetBoneObject( HandBone );
		if ( boneObject is not null && Weapon is not null )
		{
			Weapon.SetParent( boneObject, false );
			Weapon.LocalPosition = Vector3.Zero;
			Weapon.LocalRotation = Rotation.Identity;
		}
	}

	protected override void OnUpdate()
	{
		if ( AnimHelper is null ) return;

		var aiming = Input.Down( "Attack2" );

		if ( aiming )
		{
			AnimHelper.HoldType = HoldType;
			AnimHelper.IsWeaponLowered = false;
		}
		else
		{
			AnimHelper.HoldType = CitizenAnimationHelper.HoldTypes.None;
		}

		if ( Input.Pressed( "Attack1" ) )
		{
			Fire();
		}
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
			return;
		}

		Log.Info( $"Shot hit {trace.GameObject?.Name}" );

		var hitObject = trace.GameObject;
		while ( hitObject.IsValid() )
		{
			var unit = hitObject.Components.Get<UnitComponent>();
			if ( unit.IsValid() )
			{
				unit.Damage( Damage );
				return;
			}
			hitObject = hitObject.Parent;
		}
	}
}
