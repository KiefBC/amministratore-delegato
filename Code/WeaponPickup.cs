using Sandbox;
using Sandbox.Citizen;

public sealed class WeaponPickup : Component
{
	/// <summary>
	/// Hold type the player adopts when picking up this weapon.
	/// </summary>
	[Property]
	public CitizenAnimationHelper.HoldTypes HoldType { get; set; }
		= CitizenAnimationHelper.HoldTypes.Pistol;

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
}