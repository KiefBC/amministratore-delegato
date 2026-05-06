using Sandbox;

/// <summary>
/// Physical cash. As a world pickup: walking up + Use deposits the StackCount into
/// <see cref="EconomySystem"/>. As a bag slot: a Backpack-managed mirror of the
/// canonical balance — its StackCount tracks <c>EconomySystem.Money</c> via
/// <see cref="Backpack"/> polling the synced economy value. Each unit is worth
/// $1, so the slot's total worth equals StackCount.
/// </summary>
public sealed class MoneyPickup : BaseItem, IInteractable
{
	[Property]
	[Range( 10f, 500f )]
	[Step( 10f )]
	public float PickupRange { get; set; } = 100f;

	public MoneyPickup()
	{
		DisplayName = "Money";
		Value = 1;
		Weight = 0;
		MaxStack = int.MaxValue;
	}

	public override bool CanStackWith( BaseItem other ) => other is MoneyPickup;

	/// <summary>
	/// Right-click on the bag's money slot does nothing — money in the bag is already
	/// the player's spendable balance, no "consume" step needed.
	/// </summary>
	public override void OnUse( GameObject player )
	{
	}

	Vector3 IInteractable.InteractPosition => WorldPosition;
	float IInteractable.InteractRange => PickupRange;
	string IInteractable.Prompt => $"Press E to Take ${StackCount}";
	bool IInteractable.CanInteract( GameObject player ) => true;

	void IInteractable.Interact( GameObject player )
	{
		if ( !player.IsValid() ) return;

		var amount = StackCount;
		if ( amount <= 0 ) return;

		EconomySystem.Current?.Add( amount );
		GameObject.Destroy();
	}
}
