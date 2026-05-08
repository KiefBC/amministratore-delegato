using Sandbox;

/// <summary>
/// RPC entry points for gameplay systems. Keep network transport here so scene
/// systems can stay focused on gameplay rules and local scene queries.
/// </summary>
public static class GameNetworkRpc
{
	[Rpc.Broadcast]
	public static void BroadcastDamaged( GameObject targetGo, GameObject attacker, GameObject weapon, float damage, Vector3 position, Vector3 origin, bool wasKill )
	{
		if ( !CallerIsHost() ) return;

		var info = new DamageInfo
		{
			Attacker = attacker,
			Weapon = weapon,
			Damage = damage,
			Position = position,
			Origin = origin,
		};

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
	public static void RequestSetRunning( GameObject player, bool running )
	{
		if ( !CallerOwns( player ) ) return;

		UnitFor( player )?.SetRunStaminaDrain( running );
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
	public static void RequestSetWeaponAiming( GameObject player, bool aiming )
	{
		if ( !CallerOwns( player ) ) return;

		BackpackFor( player )?.TrySetWeaponAiming( aiming );
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
		if ( !CallerIsHost() ) return;
		if ( !player.IsValid() ) return;

		WeaponBehavior.SpawnDebugMarker( player.Scene, position, hit ? Color.Red : Color.Yellow );
	}

	[Rpc.Broadcast]
	public static void BroadcastNotification( int kind, string title, string message, float shownDuration )
	{
		if ( !CallerIsHost() ) return;

		NotificationSystem.Current?.NotifyFromNetwork( kind, title, message, shownDuration );
	}

	[Rpc.Broadcast]
	public static void BroadcastPlayerNotification( GameObject player, int kind, string title, string message, float shownDuration )
	{
		if ( !CallerIsHost() ) return;
		if ( !Sandbox.LocalPlayer.Owns( player ) ) return;

		NotificationSystem.Current?.NotifyFromNetwork( kind, title, message, shownDuration );
	}

	[Rpc.Host]
	public static void RequestSendChatMessage( GameObject player, string message )
	{
		if ( !CallerOwns( player ) ) return;

		ChatSystem.Current?.TrySendMessage( player, message, Rpc.Caller );
	}

	[Rpc.Broadcast]
	public static void BroadcastChatMessage( GameObject sender, string senderName, string message )
	{
		if ( !CallerIsHost() ) return;

		ChatSystem.Current?.AddNetworkMessage( sender, senderName, message );
	}

	private static Backpack BackpackFor( GameObject player )
	{
		return player?.Components.GetInDescendantsOrSelf<Backpack>();
	}

	private static UnitComponent UnitFor( GameObject player )
	{
		return player?.Components.GetInDescendantsOrSelf<UnitComponent>();
	}

	private static bool CallerOwns( GameObject gameObject )
	{
		if ( !gameObject.IsValid() ) return false;
		if ( Rpc.Caller is null ) return Networking.IsHost && Sandbox.LocalPlayer.Owns( gameObject );

		var owner = gameObject.Network.Owner;
		if ( owner is not null ) return owner == Rpc.Caller;

		var root = gameObject.Root;
		if ( root.IsValid() && root != gameObject )
		{
			owner = root.Network.Owner;
			if ( owner is not null ) return owner == Rpc.Caller;
		}

		return Rpc.Caller.IsHost && Networking.IsHost && Sandbox.LocalPlayer.Owns( gameObject );
	}

	private static bool CallerIsHost()
	{
		return Rpc.Caller is null ? Networking.IsHost : Rpc.Caller.IsHost;
	}
}
