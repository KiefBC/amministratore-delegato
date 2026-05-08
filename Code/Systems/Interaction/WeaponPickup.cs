using Sandbox;

namespace Sandbox.Systems.Interaction;

/// <summary>
/// Networked world pickup for a weapon. Inventory state is stored as synced item
/// records on <see cref="Backpack"/>; this component only represents the physical
/// world object while it exists in the scene.
/// </summary>
public sealed class WeaponPickup : Component, IInteractable
{
	[Property] public ItemDefinition Definition { get; set; }
	[Property] public string DefinitionPath { get; set; } = ItemDefinition.GlockPath;
	[Property] public int StartingAmmo { get; set; } = -1;
	private bool _networkReady;
	private bool _networkWarningLogged;

	[Property, Range( 10f, 500f ), Step( 10f )]
	public float PickupRange { get; set; } = 100f;

	Vector3 IInteractable.InteractPosition => WorldPosition;
	float IInteractable.InteractRange => PickupRange;
	string IInteractable.Prompt => $"Press E to Pick Up {ResolvedDefinition?.DisplayName ?? "Weapon"}";

	protected override void OnStart()
	{
		EnsureNetworkObject();
	}

	protected override void OnUpdate()
	{
		if ( _networkReady ) return;

		_networkReady = WorldPickupNetworking.TrySpawnOrRefresh( GameObject, ResolvedDefinition?.DisplayName ?? "weapon pickup", ref _networkWarningLogged );
	}

	bool IInteractable.CanInteract( GameObject player )
	{
		var definition = ResolvedDefinition;
		return player.IsValid() && definition is not null && definition.IsWeapon;
	}

	void IInteractable.Interact( GameObject player )
	{
		if ( !player.IsValid() ) return;
		if ( !Sandbox.Networking.IsHost ) return;

		var backpack = player.Components.GetInDescendantsOrSelf<Backpack>();
		if ( !backpack.IsValid() ) return;

		var definitionPath = ResolvedDefinitionPath;
		if ( string.IsNullOrWhiteSpace( definitionPath ) ) return;
		var definition = ResolvedDefinition;
		if ( !backpack.TryAddDefinition( definitionPath, 1, StartingAmmo, autoEquipFirstWeapon: true ) )
		{
			return;
		}

		GameLogSystem.Current?.Info( "inventory", "Weapon pickup collected", player, data: GameLogSystem.Fields(
			("definition", definitionPath),
			("displayName", definition.DisplayName),
			("startingAmmo", StartingAmmo) ) );
		PickupNotification.NotifyPickedUp( player, definition, 1 );
		GameObject.Destroy();
	}

	private string ResolvedDefinitionPath => !string.IsNullOrWhiteSpace( DefinitionPath )
		? DefinitionPath
		: ItemDefinition.PathFor( Definition );

	private ItemDefinition ResolvedDefinition => ItemDefinition.Resolve( ResolvedDefinitionPath );

	private void EnsureNetworkObject()
	{
		WorldPickupNetworking.Configure( GameObject );
	}
}
