using Sandbox;

/// <summary>
/// Scene-scoped damage dispatcher. Routes <see cref="DamageInfo"/> to a target's
/// <see cref="Component.IDamageable"/> on the host, then asks the networking
/// bridge to broadcast a local hit reaction event on every client.
///
/// Usage from anywhere: <c>CombatSystem.Current.DealDamage( target, info );</c>
/// </summary>
public sealed class CombatSystem : GameObjectSystem<CombatSystem>
{
	public CombatSystem( Scene scene ) : base( scene )
	{
	}

	/// <summary>
	/// Apply damage to a target and notify local hit-reaction components. The host applies damage;
	/// non-host callers forward the request through <see cref="GameNetworkRpc"/>.
	/// </summary>
	public void DealDamage( Component.IDamageable target, in DamageInfo info )
	{
		if ( target is null ) return;

		var targetGo = (target as Component)?.GameObject;
		if ( !Networking.IsHost )
		{
			GameNetworkRpc.RequestDealDamage( targetGo, info );
			return;
		}

		target.OnDamage( in info );

		GameNetworkRpc.BroadcastDamaged( targetGo, info );
	}

	public void NotifyDamaged( GameObject targetGo, DamageInfo info )
	{
		var resolved = targetGo?.Components.GetInAncestorsOrSelf<Component.IDamageable>();
		Scene.RunEvent<IDamagedListener>( l => l.OnDamaged( resolved, info ) );
	}
}
