using Sandbox;

/// <summary>
/// Implement on any component (HUD ammo readout, sound, animation) that wants to
/// react when a weapon's state machine transitions or its ammo count changes.
/// Broadcast via Scene.RunEvent&lt;IWeaponStateChangedListener&gt; by
/// <see cref="WeaponBehavior"/> on fire / reload-start / reload-complete.
/// </summary>
public interface IWeaponStateChangedListener
{
	void OnWeaponStateChanged( WeaponBehavior weapon );
}
