using Sandbox;
using Sandbox.Citizen;

public sealed class WeaponPickup : Component, IInteractable
{
	/// <summary>
	/// Hold type the player adopts when picking up this weapon.
	/// </summary>
	[Property]
	public CitizenAnimationHelper.HoldTypes HoldType { get; set; }
		= CitizenAnimationHelper.HoldTypes.Pistol;

	/// <summary>
	/// One-handed (Right/Left) or two-handed (Both). Only affects Pistol/HoldItem holdtypes.
	/// </summary>
	[Property]
	public CitizenAnimationHelper.Hand Handedness { get; set; }
		= CitizenAnimationHelper.Hand.Right;

	/// <summary>
	/// How close the player must be (world units) for the prompt to appear and Use to equip.
	/// </summary>
	[Property]
	[Range( 10f, 500f )]
	[Step( 10f )]
	public float PickupRange { get; set; } = 100f;

	/// <summary>
	/// Damage per shot when this weapon is held.
	/// </summary>
	[Property]
	[Range( 1f, 200f )]
	[Step( 1f )]
	public float Damage { get; set; } = 25f;

	/// <summary>
	/// Maximum trace range when this weapon is held.
	/// </summary>
	[Property]
	[Range( 100f, 10000f )]
	[Step( 100f )]
	public float Range { get; set; } = 5000f;

	/// <summary>
	/// Rounds in a full magazine. Picked-up weapons start with a full mag.
	/// </summary>
	[Property]
	[Range( 1, 100 )]
	[Step( 1 )]
	public int MagazineSize { get; set; } = 7;

	/// <summary>
	/// Seconds spent in the Reloading state before ammo refills.
	/// </summary>
	[Property]
	[Range( 0.1f, 5f )]
	[Step( 0.1f )]
	public float ReloadDuration { get; set; } = 1.5f;

	/// <summary>
	/// Local-space position offset to apply when this weapon is held in the hand bone.
	/// Tune in the editor for each weapon — pistol vs rifle vs bat all need different grips.
	/// </summary>
	[Property]
	public Vector3 WeaponOffset { get; set; } = Vector3.Zero;

	/// <summary>
	/// Local-space rotation offset (pitch/yaw/roll, degrees) to apply when held.
	/// </summary>
	[Property]
	public Angles WeaponAngleOffset { get; set; } = Angles.Zero;

	/// <summary>
	/// Local-space scale to apply when held (use 1,1,1 for no scaling).
	/// </summary>
	[Property]
	public Vector3 WeaponScale { get; set; } = Vector3.One;

	Vector3 IInteractable.InteractPosition => WorldPosition;
	float IInteractable.InteractRange => PickupRange;
	string IInteractable.Prompt => "Press E to Equip";
	bool IInteractable.CanInteract( GameObject player ) => true;

	void IInteractable.Interact( GameObject player )
	{
		if ( !player.IsValid() ) return;
		var inventory = player.Components.GetInDescendantsOrSelf<Inventory>();
		inventory?.Equip( GameObject );
	}
}