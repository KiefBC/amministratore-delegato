using Sandbox;

/// <summary>
/// Owns the player's currently equipped world item — what's parented to the hand
/// bone, what behaviors are active, what stats apply. Decoupled from <see cref="WeaponBehavior"/>:
/// equipment says "this thing is held", behavior components (WeaponBehavior for firearms,
/// future MeleeBehavior, etc.) read from equipment and react when their kind is equipped.
///
/// Lives on the player Body GameObject (alongside <see cref="WeaponBehavior"/> and
/// <see cref="Backpack"/>). Distinct from Backpack: Equipment is the one slot parented
/// to <c>hold_R</c> with active behavior; Backpack is bag storage.
/// </summary>
public sealed class Equipment : Component
{
	/// <summary>
	/// Skinned model that exposes the hand bone we attach equipped items to.
	/// Auto-discovered on this GameObject in OnStart if not wired.
	/// </summary>
	[Property]
	public SkinnedModelRenderer BodyRenderer { get; set; }

	/// <summary>
	/// Bone name on <see cref="BodyRenderer"/> to which equipped items are parented.
	/// </summary>
	[Property]
	public string HandBone { get; set; } = "hold_R";

	/// <summary>
	/// Currently held GameObject (parented to the hand bone), or null.
	/// </summary>
	public GameObject Equipped { get; private set; }

	protected override void OnStart()
	{
		BodyRenderer ??= Components.Get<SkinnedModelRenderer>();
	}

	/// <summary>
	/// Equip a world item (or a bag item being pulled out). Drops whatever is currently
	/// equipped back to the Backpack, parents the new item to the hand bone, copies its
	/// weapon stats onto the active <see cref="WeaponBehavior"/>, and disables its
	/// colliders. The item's <see cref="WeaponPickup"/> Component is preserved so
	/// metadata (Value, Weight, Rarity) stays attached for future drops/swaps.
	/// </summary>
	public void Equip( GameObject worldItem )
	{
		if ( !worldItem.IsValid() ) return;
		if ( !BodyRenderer.IsValid() ) return;

		var pickup = worldItem.Components.Get<WeaponPickup>();
		if ( !pickup.IsValid() ) return;

		var bone = BodyRenderer.GetBoneObject( HandBone );
		if ( bone is null ) return;

		// Send the previously-held item back to the bag before attaching the new one.
		Drop();

		worldItem.SetParent( bone, false );
		worldItem.LocalPosition = pickup.WeaponOffset;
		worldItem.LocalRotation = Rotation.From( pickup.WeaponAngleOffset );
		worldItem.LocalScale = pickup.WeaponScale;
		worldItem.Enabled = true;

		var renderer = worldItem.Components.Get<ModelRenderer>();
		if ( renderer.IsValid() ) renderer.Enabled = true;

		foreach ( var col in worldItem.Components.GetAll<Collider>() )
		{
			col.Enabled = false;
		}

		var behavior = Components.Get<WeaponBehavior>();
		if ( behavior.IsValid() )
		{
			behavior.HoldType = pickup.HoldType;
			behavior.Handedness = pickup.Handedness;
			behavior.Damage = pickup.Damage;
			behavior.Range = pickup.Range;
			behavior.MagazineSize = pickup.MagazineSize;
			behavior.ReloadDuration = pickup.ReloadDuration;
			behavior.WeaponOffset = pickup.WeaponOffset;
			behavior.WeaponAngleOffset = pickup.WeaponAngleOffset;
			behavior.WeaponScale = pickup.WeaponScale;
			behavior.OnEquipped();
		}

		Equipped = worldItem;

		Log.Info( $"Equipped {worldItem.Name}" );
		Scene.RunEvent<IEquipmentChangedListener>( l => l.OnEquipmentChanged( this, Equipped ) );
	}

	/// <summary>
	/// Clear the equipment slot without sending the item back to the bag. Used by
	/// <see cref="Backpack.DropToWorld"/> when the held weapon itself is the slot
	/// being dropped — the bag doesn't need to receive it (it's about to leave the
	/// player entirely).
	/// </summary>
	public void UnequipWithoutStoring()
	{
		if ( !Equipped.IsValid() ) return;
		Equipped = null;
		Scene.RunEvent<IEquipmentChangedListener>( l => l.OnEquipmentChanged( this, null ) );
	}

	/// <summary>
	/// Unequip the currently held item and route it back to the Backpack as a stored
	/// (renderer-disabled) bag item. Callers usually invoke <see cref="Equip"/>
	/// instead, which calls Drop internally before attaching the new item.
	/// </summary>
	public void Drop()
	{
		if ( !Equipped.IsValid() )
		{
			Equipped = null;
			return;
		}

		var dropped = Equipped;
		Equipped = null;

		var backpack = Components.Get<Backpack>();
		var item = dropped.Components.Get<BaseItem>();
		if ( backpack.IsValid() && item.IsValid() && backpack.StoreFromHand( item ) )
		{
			Scene.RunEvent<IEquipmentChangedListener>( l => l.OnEquipmentChanged( this, null ) );
			return;
		}

		dropped.Destroy();
		Scene.RunEvent<IEquipmentChangedListener>( l => l.OnEquipmentChanged( this, null ) );
	}
}
