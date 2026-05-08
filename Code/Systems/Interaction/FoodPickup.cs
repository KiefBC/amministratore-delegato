using Sandbox;

/// <summary>
/// Networked world pickup for food and drink items. Interacting adds the item to
/// the player's synced backpack; effects are applied later through inventory use.
/// </summary>
public sealed class FoodPickup : Component, IInteractable
{
	[Property]
	[Description( "Optional direct consumable item asset. If assigned, this takes priority over Definition Path." )]
	public ItemDefinition Definition { get; set; }

	[Property]
	[Description( "Fallback runtime resource path for the consumable item to add, for example items/water.item." )]
	public string DefinitionPath { get; set; } = ItemDefinition.WaterPath;

	[Property]
	[Description( "How many of this consumable to add to the player's backpack stack when picked up." )]
	public int Amount { get; set; } = 1;

	[Property, Range( 10f, 500f ), Step( 10f )]
	[Description( "Maximum distance, in world units, where the pickup prompt appears and host interaction is accepted." )]
	public float PickupRange { get; set; } = 100f;

	Vector3 IInteractable.InteractPosition => WorldPosition;
	float IInteractable.InteractRange => PickupRange;
	string IInteractable.Prompt => $"Press E to Pick Up {ResolvedDefinition?.DisplayName ?? "Food"}";

	bool IInteractable.CanInteract( GameObject player )
	{
		var definition = ResolvedDefinition;
		return player.IsValid() && Amount > 0 && definition is not null && definition.IsConsumable;
	}

	void IInteractable.Interact( GameObject player )
	{
		if ( !player.IsValid() ) return;
		if ( !Networking.IsHost ) return;
		if ( Amount <= 0 ) return;

		var backpack = player.Components.GetInDescendantsOrSelf<Backpack>();
		if ( !backpack.IsValid() ) return;

		var definitionPath = ResolvedDefinitionPath;
		if ( string.IsNullOrWhiteSpace( definitionPath ) ) return;
		var definition = ResolvedDefinition;
		if ( definition?.IsConsumable != true ) return;

		if ( !backpack.TryAddDefinition( definitionPath, Amount, autoEquipFirstWeapon: false ) )
		{
			return;
		}

		Log.Info( $"[FoodPickup] {PlayerLogName( player )} picked up {definition.DisplayName} x{Amount:N0}." );
		GameLogSystem.Current?.Info( "inventory", "Food pickup collected", player, data: GameLogSystem.Fields(
			("definition", definitionPath),
			("displayName", definition.DisplayName),
			("amount", Amount) ) );
		PickupNotification.NotifyPickedUp( player, definition, Amount );
		GameObject.Destroy();
	}

	private static string PlayerLogName( GameObject player )
	{
		return player.IsValid() && !string.IsNullOrWhiteSpace( player.Name ) ? player.Name : "unknown player";
	}

	private string ResolvedDefinitionPath => !string.IsNullOrWhiteSpace( ItemDefinition.PathFor( Definition ) )
		? ItemDefinition.PathFor( Definition )
		: DefinitionPath;

	private ItemDefinition ResolvedDefinition => ItemDefinition.Resolve( ResolvedDefinitionPath );
}
