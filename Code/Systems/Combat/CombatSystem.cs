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
	/// Apply damage to a target and notify local hit-reaction components. Only the
	/// host can apply damage; clients must request a validated gameplay action such
	/// as firing the equipped weapon.
	/// </summary>
	public void DealDamage( Component.IDamageable target, in DamageInfo info )
	{
		if ( target is null ) return;
		if ( !Networking.IsHost ) return;

		var targetGo = (target as Component)?.GameObject;
		var beforeUnit = target as UnitComponent;
		var wasAlive = beforeUnit is null || !beforeUnit.IsDead;
		target.OnDamage( in info );

		var afterUnit = target as UnitComponent;
		var wasKill = wasAlive && afterUnit is not null && afterUnit.IsDead;
		GameNetworkRpc.BroadcastDamaged( targetGo, info.Attacker, info.Weapon, info.Damage, info.Position, info.Origin, wasKill );
	}

	public void NotifyDamaged( GameObject targetGo, DamageInfo info, bool wasKill )
	{
		var resolved = targetGo?.Components.GetInAncestorsOrSelf<Component.IDamageable>();
		Scene.RunEvent<IDamagedListener>( l => l.OnDamaged( resolved, info, wasKill ) );
	}
}
