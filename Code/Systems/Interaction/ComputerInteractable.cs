using Sandbox;

namespace Sandbox.Systems.Interaction;

/// <summary>
/// Computer terminal entry point. Interaction is host-validated, then the owning
/// client opens a local-only finance UI.
/// </summary>
public sealed class ComputerInteractable : Component, IInteractable
{
	[Property]
	[Range( 10f, 500f )]
	[Step( 10f )]
	public float Range { get; set; } = 80f;

	Vector3 IInteractable.InteractPosition => WorldPosition;
	float IInteractable.InteractRange => Range;
	string IInteractable.Prompt => "Press E to Use Computer";
	bool IInteractable.CanInteract( GameObject player ) => player.IsValid();

	void IInteractable.Interact( GameObject player )
	{
		if ( !Sandbox.Networking.IsHost ) return;

		if ( Sandbox.Systems.Movement.LocalPlayer.Owns( player ) )
		{
			ComputerTerminalSystem.OpenForScene( Scene );
			return;
		}

		GameNetworkRpc.BroadcastOpenComputerTerminal( player );
	}
}
