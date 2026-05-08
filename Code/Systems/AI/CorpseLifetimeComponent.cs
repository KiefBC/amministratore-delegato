using Sandbox;

namespace Sandbox.Systems.AI;

/// <summary>
/// Host-side lifetime for networked corpse presentation objects.
/// </summary>
public sealed class CorpseLifetimeComponent : Component
{
	[Property]
	public float LifetimeSeconds { get; set; } = 60f;

	private float _destroyTime;

	protected override void OnStart()
	{
		if ( !Sandbox.Networking.IsHost ) return;

		_destroyTime = Time.Now + float.Max( 0f, LifetimeSeconds );
	}

	protected override void OnUpdate()
	{
		if ( !Sandbox.Networking.IsHost ) return;
		if ( _destroyTime <= 0f || Time.Now < _destroyTime ) return;

		GameObject.Destroy();
	}
}
