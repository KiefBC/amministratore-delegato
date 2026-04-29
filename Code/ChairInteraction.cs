using Sandbox;

/// <summary>
/// Deprecated — sit/stand is now <see cref="SittableInteractable"/> on each chair, dispatched by
/// <see cref="InteractionSystem"/>. This empty stub remains so legacy scene wiring still resolves
/// (the disabled component on the player Body, and the SitPrompt's binding); both will be
/// removed when scenes are re-saved.
/// </summary>
public sealed class ChairInteraction : Component
{
	[Property] public PlayerController Controller { get; set; }

	public BaseChair NearbyChair => null;
	public bool IsSitting => false;
}
