using Sandbox;

/// <summary>
/// Stub interactable that proves the seam works: implements <see cref="IInteractable"/>,
/// no edits to <see cref="InteractionSystem"/>, <c>InteractPrompt.razor</c>, <c>Inventory</c>,
/// or any other file required. Same pattern any future interactable (ATM, locker, vehicle door,
/// dropped item) follows — one component, one file.
///
/// To test: add this component to any empty GameObject in the scene, walk near it, press E.
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
	bool IInteractable.CanInteract( GameObject player ) => true;

	void IInteractable.Interact( GameObject player )
	{
		Log.Info( $"[Computer] Used by {player?.Name ?? "null"} on {GameObject.Name}" );
	}
}
