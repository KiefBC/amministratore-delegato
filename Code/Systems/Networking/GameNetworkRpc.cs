using Sandbox;

/// <summary>
/// RPC entry points for gameplay systems. Keep network transport here so scene
/// systems can stay focused on gameplay rules and local scene queries.
/// </summary>
public static class GameNetworkRpc
{
	[Rpc.Host]
	public static void RequestDealDamage( GameObject targetGo, DamageInfo info )
	{
		if ( !CallerOwns( info.Attacker ) ) return;

		var target = targetGo?.Components.GetInAncestorsOrSelf<Component.IDamageable>();
		CombatSystem.Current?.DealDamage( target, in info );
	}

	[Rpc.Broadcast]
	public static void BroadcastDamaged( GameObject targetGo, DamageInfo info )
	{
		CombatSystem.Current?.NotifyDamaged( targetGo, info );
	}

	[Rpc.Host]
	public static void RequestInteract( GameObject targetGo, GameObject player )
	{
		if ( !CallerOwns( player ) ) return;

		InteractionSystem.Current?.TryInteractOnHost( targetGo, player );
	}

	[Rpc.Host]
	public static void RequestSpendMoney( int amount )
	{
		EconomySystem.Current?.TrySpend( amount );
	}

	private static bool CallerOwns( GameObject gameObject )
	{
		if ( !gameObject.IsValid() ) return false;

		var owner = gameObject.Network.Owner;
		if ( owner is not null ) return owner == Rpc.Caller;

		var root = gameObject.Root;
		if ( !root.IsValid() || root == gameObject ) return false;

		owner = root.Network.Owner;
		return owner is not null && owner == Rpc.Caller;
	}
}
