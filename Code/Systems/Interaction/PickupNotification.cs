using Sandbox;

namespace Sandbox.Systems.Interaction;

public static class PickupNotification
{
	private const float PickupDuration = 3f;

	public static void NotifyPickedUp( GameObject player, string displayName, int amount = 1 )
	{
		if ( !Sandbox.Networking.IsHost ) return;
		if ( !player.IsValid() ) return;

		displayName = string.IsNullOrWhiteSpace( displayName ) ? "Item" : displayName.Trim();

		GameNetworkRpc.BroadcastPlayerNotification(
			player,
			(int)NotificationKind.Success,
			"Picked Up",
			FormatPickupMessage( displayName, amount ),
			PickupDuration );
	}

	public static void NotifyPickedUp( GameObject player, ItemDefinition definition, int amount = 1 )
	{
		NotifyPickedUp( player, definition?.DisplayName ?? "Item", amount );
	}

	public static void NotifyMoneyPickedUp( GameObject player, int amount )
	{
		if ( amount <= 0 ) return;

		NotifyPickedUp( player, $"${amount:N0}", 1 );
	}

	public static void NotifyMoneyPickupFailed( GameObject player, int amount )
	{
		if ( !Sandbox.Networking.IsHost ) return;
		if ( !player.IsValid() ) return;
		if ( amount <= 0 ) return;

		GameNetworkRpc.BroadcastPlayerNotification(
			player,
			(int)NotificationKind.Warning,
			"Inventory Full",
			$"Free a slot to pick up ${amount:N0}.",
			PickupDuration );
	}

	private static string FormatPickupMessage( string displayName, int amount )
	{
		return amount > 1
			? $"{displayName} x{amount:N0}"
			: displayName;
	}
}
