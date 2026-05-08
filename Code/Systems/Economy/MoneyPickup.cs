using Sandbox;

/// <summary>
/// Networked world pickup for cash. The host deposits it into the interacting
/// player's synced <see cref="Backpack.Wallet"/>, then destroys this world object.
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
		if ( !Networking.IsHost ) return;
		if ( Amount <= 0 ) return;

		EconomySystem.Current?.Add( player, Amount );
		PickupNotification.NotifyMoneyPickedUp( player, Amount );
		GameObject.Destroy();
	}
}
