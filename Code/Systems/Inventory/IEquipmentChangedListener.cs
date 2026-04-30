using Sandbox;

/// <summary>
/// Implement on any component (HUD weapon indicator, sound, future weapon-skill tracker)
/// that wants to react when a player's equipped item changes. Broadcast via
/// Scene.RunEvent&lt;IEquipmentChangedListener&gt; by <see cref="Inventory"/> after Equip / Drop.
/// </summary>
public interface IEquipmentChangedListener
{
	/// <summary>
	/// Fired when the inventory's equipped item changes. <paramref name="equipped"/> is null
	/// when the slot is now empty (e.g. after Drop).
	/// </summary>
	void OnEquipmentChanged( Inventory inventory, GameObject equipped );
}
