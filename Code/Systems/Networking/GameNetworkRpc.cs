using Sandbox;

/// <summary>
/// RPC entry points for gameplay systems. Keep network transport here so scene
/// systems can stay focused on gameplay rules and local scene queries.
/// </summary>
public static class GameNetworkRpc
{
	[Rpc.Broadcast]
	public static void BroadcastDamaged( GameObject targetGo, DamageInfo info, bool wasKill )
	{
		CombatSystem.Current?.NotifyDamaged( targetGo, info, wasKill );
	}

	[Rpc.Host]
	public static void RequestInteract( GameObject targetGo, GameObject player )
	{
		if ( !CallerOwns( player ) ) return;

		InteractionSystem.Current?.TryInteractOnHost( targetGo, player );
	}

	[Rpc.Host]
	public static void RequestUseInventorySlot( GameObject player, int slot )
	{
		if ( !CallerOwns( player ) ) return;

		BackpackFor( player )?.TryUseSlot( slot );
	}

	[Rpc.Host]
	public static void RequestMoveInventorySlot( GameObject player, int fromSlot, int toSlot )
	{
		if ( !CallerOwns( player ) ) return;

		BackpackFor( player )?.TryMoveSlot( fromSlot, toSlot );
	}

	[Rpc.Host]
	public static void RequestDropInventorySlot( GameObject player, int slot )
	{
		if ( !CallerOwns( player ) ) return;

		BackpackFor( player )?.TryDropSlot( slot );
	}

	[Rpc.Host]
	public static void RequestSortInventory( GameObject player, int sortMode )
	{
		if ( !CallerOwns( player ) ) return;
		if ( !System.Enum.IsDefined( typeof( Backpack.SortMode ), sortMode ) ) return;

		BackpackFor( player )?.TrySort( (Backpack.SortMode)sortMode );
	}

	[Rpc.Host]
	public static void RequestSpendMoney( GameObject player, int amount )
	{
		if ( !CallerOwns( player ) ) return;

		EconomySystem.Current?.TrySpend( player, amount );
	}

	[Rpc.Host]
	public static void RequestToggleHolster( GameObject player )
	{
		if ( !CallerOwns( player ) ) return;

		BackpackFor( player )?.TryToggleHolster();
	}

	[Rpc.Host]
	public static void RequestReloadWeapon( GameObject player )
	{
		if ( !CallerOwns( player ) ) return;

		BackpackFor( player )?.TryStartReload();
	}

	[Rpc.Host]
	public static void RequestFireWeapon( GameObject player, Vector3 origin, Rotation aim )
	{
		if ( !CallerOwns( player ) ) return;

		BackpackFor( player )?.TryFire( origin, aim );
	}

	[Rpc.Broadcast]
	public static void BroadcastShotDebug( GameObject player, Vector3 position, bool hit )
	{
		if ( !player.IsValid() ) return;

		WeaponBehavior.SpawnDebugMarker( player.Scene, position, hit ? Color.Red : Color.Yellow );
	}

	private static Backpack BackpackFor( GameObject player )
	{
		return player?.Components.GetInDescendantsOrSelf<Backpack>();
	}

	private static bool CallerOwns( GameObject gameObject )
	{
		if ( !gameObject.IsValid() ) return false;
		if ( Networking.IsHost && Sandbox.LocalPlayer.Owns( gameObject ) ) return true;

		var owner = gameObject.Network.Owner;
		if ( owner is not null ) return owner == Rpc.Caller;

		var root = gameObject.Root;
		if ( !root.IsValid() || root == gameObject ) return false;

		owner = root.Network.Owner;
		return owner is not null && owner == Rpc.Caller;
	}
}
