using Sandbox;

/// <summary>
/// Scene-scoped: every frame, finds the nearest valid <see cref="IInteractable"/>
/// to the local player and dispatches the Use press. Replaces the per-component
/// "find nearest thing in range" loops that used to live duplicated in
/// pickup/chair components.
///
/// Usage: any component implementing <see cref="IInteractable"/> participates
/// automatically — no registration needed. UI reads <see cref="CurrentTarget"/>.
/// </summary>
public sealed class InteractionSystem : GameObjectSystem<InteractionSystem>
{
	/// <summary>
	/// The player we are computing interactions for. Auto-discovered each tick if null
	/// by finding a non-proxy <see cref="PlayerController"/> in the scene. Can be
	/// overridden externally for split-screen or test setups.
	/// </summary>
	public GameObject LocalPlayer { get; set; }

	/// <summary>
	/// Closest valid interactable to <see cref="LocalPlayer"/> this frame, or null.
	/// </summary>
	public IInteractable CurrentTarget { get; private set; }

	public InteractionSystem( Scene scene ) : base( scene )
	{
		Listen( Stage.StartUpdate, 0, OnTick, nameof( OnTick ) );
	}

	private void OnTick()
	{
		CurrentTarget = null;

		if ( !Sandbox.LocalPlayer.Owns( LocalPlayer ) )
		{
			LocalPlayer = FindLocalPlayer();
			if ( !LocalPlayer.IsValid() ) return;
		}

		var playerPos = LocalPlayer.WorldPosition;
		var bestDistSq = float.MaxValue;
		IInteractable best = null;

		foreach ( var i in Scene.GetAllComponents<IInteractable>() )
		{
			if ( i is not Component c ) continue;
			if ( !c.IsValid() ) continue;
			if ( IsAtOrBelow( c.GameObject, LocalPlayer ) ) continue;
			if ( !i.CanInteract( LocalPlayer ) ) continue;

			var distSq = i.InteractPosition.DistanceSquared( playerPos );
			var range = i.InteractRange;
			if ( distSq > range * range ) continue;

			if ( distSq < bestDistSq )
			{
				bestDistSq = distSq;
				best = i;
			}
		}

		CurrentTarget = best;

		if ( best is not null && Input.Pressed( "Use" ) )
		{
			Interact( best, LocalPlayer );
		}
	}

	private void Interact( IInteractable interactable, GameObject player )
	{
		if ( interactable is not Component component ) return;

		if ( !Networking.IsHost )
		{
			if ( interactable is IClientInteractable clientInteractable && clientInteractable.TryClientInteract( player ) ) return;

			GameNetworkRpc.RequestInteract( component.GameObject, player );
			return;
		}

		TryInteractOnHost( component.GameObject, player );
	}

	public void TryInteractOnHost( GameObject targetGo, GameObject player )
	{
		if ( !targetGo.IsValid() || !player.IsValid() ) return;

		foreach ( var interactable in targetGo.Components.GetAll<IInteractable>() )
		{
			if ( interactable is not Component component || !component.IsValid() ) continue;
			if ( !interactable.CanInteract( player ) ) continue;

			var range = interactable.InteractRange;
			if ( interactable.InteractPosition.DistanceSquared( player.WorldPosition ) > range * range ) continue;

			interactable.Interact( player );
			return;
		}
	}

	private GameObject FindLocalPlayer()
	{
		return Sandbox.LocalPlayer.GameObject( Scene );
	}

	/// <summary>
	/// True if <paramref name="candidate"/> is <paramref name="ancestor"/> itself or one of its
	/// descendants. Used to filter out interactables attached to the player (held weapon, etc.).
	/// We can't use <c>GameObject.Root</c> for this — when the player is parented under a chair
	/// while seated, the chair becomes the player's root and would incorrectly filter itself out.
	/// </summary>
	private static bool IsAtOrBelow( GameObject candidate, GameObject ancestor )
	{
		var p = candidate;
		while ( p.IsValid() )
		{
			if ( p == ancestor ) return true;
			p = p.Parent;
		}
		return false;
	}
}
