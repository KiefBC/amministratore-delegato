using Sandbox;

/// <summary>
/// Scene-scoped damage dispatcher. Routes <see cref="DamageInfo"/> to a target's
/// <see cref="Component.IDamageable"/> on the host, then broadcasts an
/// <see cref="IDamagedListener"/> event scene-wide so HUD/sound/VFX can react.
///
/// Usage from anywhere: <c>CombatSystem.Current.DealDamage( target, info );</c>
/// </summary>
public sealed class CombatSystem : GameObjectSystem<CombatSystem>
{
	public CombatSystem( Scene scene ) : base( scene )
	{
	}

	/// <summary>
	/// Apply damage to a target and notify listeners. Today this runs only when
	/// <see cref="Networking.IsHost"/> — solo dev = always host. When networking
	/// turns on, add an Rpc.Host forwarder for non-host callers.
	/// </summary>
	public void DealDamage( Component.IDamageable target, in DamageInfo info )
	{
		if ( target is null ) return;
		if ( !Networking.IsHost ) return;

		target.OnDamage( in info );

		var targetGo = ( target as Component )?.GameObject;
		BroadcastDamaged( targetGo, info );
	}

	[Rpc.Broadcast]
	private void BroadcastDamaged( GameObject targetGo, DamageInfo info )
	{
		var resolved = targetGo?.Components.GetInAncestorsOrSelf<Component.IDamageable>();
		Scene.RunEvent<IDamagedListener>( l => l.OnDamaged( resolved, info ) );
	}
}
