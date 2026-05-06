using Sandbox;

public sealed class PlayerComponent : Component
{
	protected override void OnUpdate()
	{
		if ( IsProxy ) return;

		// Stub — engine PlayerController handles movement; future custom player
		// logic (stamina, footsteps, etc.) lives here.
	}
}
