using Sandbox;

/// <summary>
/// Attach to any chair GameObject (one with a <see cref="BaseChair"/> component, or a parent of one).
/// Exposes the chair to <see cref="InteractionSystem"/> so the player can E-to-sit and E-to-stand
/// using the same shared prompt and dispatch path as weapon pickups, computers, ATMs, etc.
/// </summary>
public sealed class SittableInteractable : Component, IInteractable
{
	/// <summary>
	/// The underlying engine chair component. Auto-discovered on this GameObject (or its descendants)
	/// in OnStart if not wired in the inspector.
	/// </summary>
	[Property]
	public BaseChair Chair { get; set; }

	/// <summary>
	/// How close the player must be (world units) for the prompt to appear and Use to fire.
	/// </summary>
	[Property]
	[Range( 10f, 500f )]
	[Step( 10f )]
	public float SitRange { get; set; } = 100f;

	private GameObject _occupant = null;

	public bool IsOccupied => _occupant.IsValid();

	protected override void OnStart()
	{
		Chair ??= Components.GetInDescendantsOrSelf<BaseChair>();
	}

	Vector3 IInteractable.InteractPosition => Chair.IsValid() ? Chair.WorldPosition : WorldPosition;

	float IInteractable.InteractRange => SitRange;

	string IInteractable.Prompt => IsOccupied ? "Press E to Stand" : "Press E to Sit";

	bool IInteractable.CanInteract( GameObject player )
	{
		if ( !Chair.IsValid() ) return false;
		// While occupied, only the seated player can interact (to stand). While free, anyone can sit.
		if ( IsOccupied ) return _occupant == player;
		return true;
	}

	void IInteractable.Interact( GameObject player )
	{
		if ( !Chair.IsValid() ) return;

		if ( IsOccupied && _occupant == player )
		{
			Stand();
			return;
		}

		if ( !IsOccupied )
		{
			Sit( player );
		}
	}

	private void Sit( GameObject player )
	{
		player.SetParent( Chair.GameObject );

		if ( Chair.SeatPosition.IsValid() )
		{
			player.WorldPosition = Chair.SeatPosition.WorldPosition;
			player.WorldRotation = Chair.SeatPosition.WorldRotation;
		}

		_occupant = player;
	}

	private void Stand()
	{
		var exit = FindBestExit();
		_occupant.SetParent( null );

		if ( exit.IsValid() )
		{
			_occupant.WorldPosition = exit.WorldPosition;
			_occupant.WorldRotation = exit.WorldRotation;
		}

		_occupant = null;
	}

	private GameObject FindBestExit()
	{
		if ( !Chair.IsValid() || Chair.ExitPoints == null ) return null;

		GameObject nearest = null;
		float nearestDistance = float.MaxValue;
		var pos = _occupant.IsValid() ? _occupant.WorldPosition : WorldPosition;

		foreach ( var point in Chair.ExitPoints )
		{
			if ( !point.IsValid() ) continue;

			var dist = point.WorldPosition.Distance( pos );
			if ( dist < nearestDistance )
			{
				nearest = point;
				nearestDistance = dist;
			}
		}

		return nearest;
	}
}
