using Sandbox;

namespace Sandbox.Systems.Economy;

/// <summary>
/// Networked world pickup for cash. The host adds it as a physical money item
/// stack in the interacting player's inventory, then destroys this world object.
/// </summary>
public sealed class MoneyPickup : Component, IInteractable
{
	[Property] public ItemDefinition Definition { get; set; }
	[Property] public string DefinitionPath { get; set; } = ItemDefinition.MoneyPath;
	[Property] public int Amount { get; set; } = 1;

	[Property, Range( 10f, 500f ), Step( 10f )]
	public float PickupRange { get; set; } = 100f;

	Vector3 IInteractable.InteractPosition => WorldPosition;
	float IInteractable.InteractRange => PickupRange;
	string IInteractable.Prompt => $"Press E to Take ${Amount:N0}";
	bool IInteractable.CanInteract( GameObject player ) => player.IsValid() && Amount > 0;

	void IInteractable.Interact( GameObject player )
	{
		if ( !player.IsValid() ) return;
		if ( !Sandbox.Networking.IsHost ) return;
		if ( Amount <= 0 ) return;

		var backpack = player.Components.GetInDescendantsOrSelf<Backpack>();
		var oldWallet = backpack.IsValid() ? backpack.Wallet : 0;
		var accepted = DefinitionPath == ItemDefinition.MoneyPath
			? backpack.IsValid() && backpack.AddMoney( Amount )
			: backpack.IsValid() && backpack.TryAddDefinition( DefinitionPath, Amount, autoEquipFirstWeapon: false );

		if ( !accepted )
		{
			PickupNotification.NotifyMoneyPickupFailed( player, Amount );
			GameLogSystem.Current?.Warning( "inventory", "Money pickup rejected inventory full", player, data: GameLogSystem.Fields(
				("amount", Amount) ) );
			return;
		}

		Log.Info( $"[MoneyPickup] {PlayerLogName( player )} picked up ${Amount:N0}. Wallet ${oldWallet:N0} -> ${backpack.Wallet:N0}." );
		GameLogSystem.Current?.Info( "inventory", "Money pickup collected", player, data: GameLogSystem.Fields(
			("amount", Amount),
			("oldWallet", oldWallet),
			("newWallet", backpack.Wallet) ) );
		PickupNotification.NotifyMoneyPickedUp( player, Amount );
		GameObject.Destroy();
	}

	private static string PlayerLogName( GameObject player )
	{
		if ( !player.IsValid() ) return "unknown player";
		if ( !string.IsNullOrWhiteSpace( player.Name ) ) return player.Name;

		var root = player.Root;
		if ( root.IsValid() && !string.IsNullOrWhiteSpace( root.Name ) ) return root.Name;
		return "unnamed player";
	}
}
