/// <summary>
/// Implement on any component (inventory UI, encumbrance system, future tooltip)
/// that wants to react when the bag's contents change. Broadcast via
/// Scene.RunEvent&lt;IInventoryChangedListener&gt; by <see cref="Backpack"/> after
/// any TryAdd / MoveSlot / Remove / Sort / DropToWorld mutation.
/// </summary>
public interface IInventoryChangedListener
{
	void OnInventoryChanged( Backpack backpack );
}
