using Sandbox;

/// <summary>
/// Implement on any component (HUD weapon indicator, sound, animation) that wants to
/// react when a player's weapon is holstered or drawn. Broadcast via
/// Scene.RunEvent&lt;IHolsterChangedListener&gt; by <see cref="WeaponBehavior"/> on toggle.
/// </summary>
public interface IHolsterChangedListener
{
	void OnHolsterChanged( WeaponBehavior weapon, bool holstered );
}
