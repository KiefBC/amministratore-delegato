using Sandbox;

/// <summary>
/// Owns the player's currently equipped world item — what's parented to the hand
/// bone, what behaviors are active, what stats apply. Decoupled from <see cref="WeaponBehavior"/>:
/// inventory says "this thing is held", behavior components (WeaponBehavior for firearms,
/// future MeleeBehavior, etc.) read from inventory and react when their kind is equipped.
///
/// Lives on the player Body GameObject (alongside <see cref="WeaponBehavior"/>).
/// </summary>
public sealed class Inventory : Component
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
	/// Pick up a world item. Drops whatever is currently equipped, parents the new
	/// item to the hand bone, copies its weapon stats onto the active <see cref="WeaponBehavior"/>
	/// behavior, disables its colliders, and removes the <see cref="WeaponPickup"/> marker.
	/// </summary>
	public void Equip( GameObject worldItem )
	{
		if ( !worldItem.IsValid() ) return;
		if ( !BodyRenderer.IsValid() ) return;

		var pickup = worldItem.Components.Get<WeaponPickup>();
		if ( !pickup.IsValid() ) return;

		var bone = BodyRenderer.GetBoneObject( HandBone );
		if ( bone is null ) return;

		Drop();

		worldItem.SetParent( bone, false );
		worldItem.LocalPosition = pickup.WeaponOffset;
		worldItem.LocalRotation = Rotation.From( pickup.WeaponAngleOffset );
		worldItem.LocalScale = pickup.WeaponScale;

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
			behavior.WeaponOffset = pickup.WeaponOffset;
			behavior.WeaponAngleOffset = pickup.WeaponAngleOffset;
			behavior.WeaponScale = pickup.WeaponScale;
		}

		Equipped = worldItem;
		pickup.Destroy();

		Log.Info( $"Equipped {worldItem.Name}" );
	}

	/// <summary>
	/// Destroy the currently equipped item. Stub for now — future "drop to ground"
	/// would re-enable colliders, unparent, and reattach a WeaponPickup component.
	/// </summary>
	public void Drop()
	{
		if ( !Equipped.IsValid() ) return;
		Equipped.Destroy();
		Equipped = null;
	}
}
