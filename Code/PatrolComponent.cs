using Sandbox;

public sealed class PatrolComponent : Component
{
	/// <summary>
	/// First patrol waypoint
	/// </summary>
	[Property]
	public GameObject PointA { get; set; }

	/// <summary>
	/// Second patrol waypoint
	/// </summary>
	[Property]
	public GameObject PointB { get; set; }

	/// <summary>
	/// Movement speed in units per second
	/// </summary>
	[Property]
	[Range( 10f, 500f )]
	[Step( 10f )]
	public float Speed { get; set; } = 100f;

	/// <summary>
	/// How close the unit must get to a waypoint before switching to the other
	/// </summary>
	[Property]
	[Range( 1f, 100f )]
	[Step( 1f )]
	public float ArrivalDistance { get; set; } = 10f;

	/// <summary>
	/// How quickly the unit rotates to face its direction of travel
	/// </summary>
	[Property]
	[Range( 0f, 20f )]
	[Step( 0.5f )]
	public float TurnSpeed { get; set; } = 5f;

	private GameObject _target;

	protected override void OnStart()
	{
		_target = PointA;
	}

	protected override void OnUpdate()
	{
		if ( !PointA.IsValid() || !PointB.IsValid() ) return;
		if ( !_target.IsValid() ) _target = PointA;

		var toTarget = _target.WorldPosition - WorldPosition;
		toTarget.z = 0f;

		if ( toTarget.Length < ArrivalDistance )
		{
			_target = ( _target == PointA ) ? PointB : PointA;
			return;
		}

		var direction = toTarget.Normal;
		WorldPosition += direction * Speed * Time.Delta;

		var lookRotation = Rotation.LookAt( direction, Vector3.Up );
		WorldRotation = Rotation.Slerp( WorldRotation, lookRotation, Time.Delta * TurnSpeed );
	}
}