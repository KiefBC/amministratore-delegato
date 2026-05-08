namespace Sandbox.Systems.Inventory;

public struct InventoryItemState
{
	public int InstanceId { get; set; }
	public string DefinitionPath { get; set; }
	public int StackCount { get; set; }
	public int SlotIndex { get; set; }
	public int Ammo { get; set; }
	public WeaponBehavior.WeaponState State { get; set; }
	public bool IsHolstered { get; set; }
	public float ReloadEndTime { get; set; }
	public float LastFireTime { get; set; }

	public bool IsValid => InstanceId > 0 && !string.IsNullOrWhiteSpace( DefinitionPath );
}
