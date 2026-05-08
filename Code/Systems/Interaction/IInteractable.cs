namespace Sandbox.Systems.Interaction;

/// <summary>
/// Implement on any component the player can interact with via E (chairs, weapon
/// pickups, computers, ATMs, dropped items). The <see cref="InteractionSystem"/>
/// runs one per-frame nearest-target query for the local player and dispatches
/// <see cref="Interact"/> on the Use press.
/// </summary>
public interface IInteractable
{
	/// <summary>
	/// World-space position used to compute distance to the player. Default to
	/// <c>WorldPosition</c>; override only if the trigger volume differs from the
	/// visual origin (e.g., a chair that wants distance measured at its seat).
	/// </summary>
	Vector3 InteractPosition { get; }

	/// <summary>
	/// Maximum distance from the player at which this interactable is considered.
	/// </summary>
	float InteractRange { get; }

	/// <summary>
	/// HUD prompt label, e.g. "Press E to Equip" / "Press E to Sit".
	/// May change based on state ("Press E to Stand" while seated).
	/// </summary>
	string Prompt { get; }

	/// <summary>
	/// Per-frame gating — return false to hide the prompt and reject Use.
	/// Useful for "while occupied", "needs key", "out of stock", etc.
	/// </summary>
	bool CanInteract( GameObject player );

	/// <summary>
	/// Invoked once when the player presses Use while this is the current target.
	/// </summary>
	void Interact( GameObject player );
}
