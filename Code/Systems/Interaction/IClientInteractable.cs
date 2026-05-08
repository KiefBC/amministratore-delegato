using Sandbox;

/// <summary>
/// Optional client-side dispatch for interactables that need local-only context
/// before sending a host request, such as the exact desk device under the crosshair.
/// </summary>
public interface IClientInteractable
{
	bool TryClientInteract( GameObject player );
}
