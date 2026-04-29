public sealed class ChairInteraction : Component
{
	[Property] public PlayerController Controller { get; set; }

	[Property]
	[Range( 10f, 500f )]
	[Step( 10f )]
	public float SitRange { get; set; } = 100f;

	public BaseChair NearbyChair { get; private set; }

	public bool IsSitting
		=> _currentChair.IsValid()
		&& Controller.IsValid()
		&& Controller.GameObject.Parent == _currentChair.GameObject;

	private BaseChair _currentChair;

	protected override void OnUpdate()
	{
		if ( !Controller.IsValid() ) return;

		if ( IsSitting )
		{
			NearbyChair = null;

			if ( Input.Pressed( "Use" ) )
			{
				LeaveChair();
			}

			return;
		}

		_currentChair = null;
		NearbyChair = FindNearestChair();

		if ( Input.Pressed( "Use" ) && NearbyChair.IsValid() )
		{
			EnterChair( NearbyChair );
		}
	}

	private void EnterChair( BaseChair chair )
	{
		Controller.GameObject.SetParent( chair.GameObject );

		if ( chair.SeatPosition.IsValid() )
		{
			Controller.WorldPosition = chair.SeatPosition.WorldPosition;
			Controller.WorldRotation = chair.SeatPosition.WorldRotation;
		}

		_currentChair = chair;
		NearbyChair = null;
	}

	private void LeaveChair()
	{
		var exit = FindBestExit( _currentChair );
		Controller.GameObject.SetParent( null );

		if ( exit.IsValid() )
		{
			Controller.WorldPosition = exit.WorldPosition;
			Controller.WorldRotation = exit.WorldRotation;
		}

		_currentChair = null;
	}

	private BaseChair FindNearestChair()
	{
		BaseChair nearest = null;
		float nearestDistance = SitRange;

		foreach ( var chair in Scene.GetAllComponents<BaseChair>() )
		{
			if ( !chair.IsValid() ) continue;
			if ( chair.IsOccupied ) continue;

			var dist = chair.WorldPosition.Distance( WorldPosition );
			if ( dist < nearestDistance )
			{
				nearest = chair;
				nearestDistance = dist;
			}
		}

		return nearest;
	}

	private GameObject FindBestExit( BaseChair chair )
	{
		if ( !chair.IsValid() || chair.ExitPoints == null ) return null;

		GameObject nearest = null;
		float nearestDistance = float.MaxValue;

		foreach ( var point in chair.ExitPoints )
		{
			if ( !point.IsValid() ) continue;

			var dist = point.WorldPosition.Distance( WorldPosition );
			if ( dist < nearestDistance )
			{
				nearest = point;
				nearestDistance = dist;
			}
		}

		return nearest;
	}
}
