using Sandbox;

namespace Sandbox.Systems.Combat;

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
		if ( !Sandbox.Networking.IsHost ) return;

		var targetGo = (target as Component)?.GameObject;
		var beforeUnit = target as UnitComponent;
		var wasAlive = beforeUnit is null || !beforeUnit.IsDead;
		var beforeHealth = beforeUnit?.Health ?? 0f;
		var beforeArmor = beforeUnit?.Armor ?? 0f;
		target.OnDamage( in info );

		var afterUnit = target as UnitComponent;
		var wasKill = wasAlive && afterUnit is not null && afterUnit.IsDead;
		LogDamage( targetGo, info, beforeUnit, afterUnit, beforeHealth, beforeArmor, wasKill );
		GameNetworkRpc.BroadcastDamaged( targetGo, info.Attacker, info.Weapon, info.Damage, info.Position, info.Origin, wasKill );
	}

	private static void LogDamage( GameObject targetGo, DamageInfo info, UnitComponent beforeUnit, UnitComponent afterUnit, float beforeHealth, float beforeArmor, bool wasKill )
	{
		var attackerName = GameObjectLogName( info.Attacker );
		var targetName = UnitLogName( afterUnit ?? beforeUnit, targetGo );

		if ( afterUnit is null )
		{
			Log.Info( $"[Combat] {attackerName} damaged {targetName} for {info.Damage:0.#}." );
			return;
		}

		Log.Info( $"[Combat] {attackerName} damaged {targetName} for {info.Damage:0.#}. Health {beforeHealth:0.#}->{afterUnit.Health:0.#}, armor {beforeArmor:0.#}->{afterUnit.Armor:0.#}, kill={wasKill}." );
	}

	private static string UnitLogName( UnitComponent unit, GameObject targetGo )
	{
		if ( unit.IsValid() ) return GameObjectLogName( unit.GameObject.Root );
		return GameObjectLogName( targetGo );
	}

	private static string GameObjectLogName( GameObject gameObject )
	{
		if ( gameObject.IsValid() && !string.IsNullOrWhiteSpace( gameObject.Name ) ) return gameObject.Name;
		return "unknown";
	}

	public void NotifyDamaged( GameObject targetGo, DamageInfo info, bool wasKill )
	{
		var resolved = targetGo?.Components.GetInAncestorsOrSelf<Component.IDamageable>();
		Scene.RunEvent<IDamagedListener>( l => l.OnDamaged( resolved, info, wasKill ) );

		if ( wasKill )
		{
			NotificationSystem.Current?.NotifyKill( targetGo, info.Attacker );
		}
	}
}
