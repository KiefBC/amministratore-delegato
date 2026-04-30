using Sandbox;

/// <summary>
/// Base for anything that can live in a player's <see cref="Backpack"/> — weapons,
/// money, ammo, etc. Subclasses fill in <see cref="OnUse"/> with what happens when
/// the player right-clicks the slot (equip a weapon, no-op for money, etc.).
///
/// Each item is a Component on a world GameObject; pickup parents that GameObject
/// under the backpack (with renderer + colliders disabled). The same Component
/// instance survives in the bag, so per-prefab properties (Value, Weight, Rarity,
/// Icon, plus any subclass-specific fields like weapon stats) stay attached.
/// </summary>
public abstract class BaseItem : Component
{
	[Property] public string DisplayName { get; set; } = "";

	/// <summary>
	/// Currency value of one unit. The slot's total worth (used by the Worth sort)
	/// is <c>Value * StackCount</c> — money is Value=1 with StackCount=$N, weapons
	/// are Value=$N with StackCount=1.
	/// </summary>
	[Property] public int Value { get; set; } = 0;

	/// <summary>
	/// Weight modifier per unit. Total slot weight is <c>Weight * StackCount</c>.
	/// </summary>
	[Property] public int Weight { get; set; } = 0;

	/// <summary>
	/// Asset path to the icon shown in the inventory grid cell (e.g., "ui/icons/glock.png").
	/// Editor-assigned per prefab. Empty = no icon.
	/// </summary>
	[Property] public string Icon { get; set; } = "";

	[Property] public Rarity Rarity { get; set; } = Rarity.Common;

	/// <summary>
	/// Reserved for future multi-slot items (rifle = 2 squares). Layout currently
	/// treats every item as 1×1.
	/// </summary>
	[Property] public int GridWidth { get; set; } = 1;
	[Property] public int GridHeight { get; set; } = 1;

	/// <summary>
	/// Maximum units that can stack into a single grid slot. Weapons override to 1;
	/// money/ammo override high.
	/// </summary>
	[Property] public int MaxStack { get; set; } = 1;

	/// <summary>
	/// Live unit count in the slot. Set by <see cref="Backpack"/> when stacking on add
	/// or splitting on drop. Not [Property] — runtime state, not designer-tunable.
	/// </summary>
	public int StackCount { get; set; } = 1;

	/// <summary>
	/// Set by <see cref="Backpack.TryAdd"/> when the item enters the bag. Drives the
	/// "New" sort (newest first).
	/// </summary>
	public RealTimeSince AcquiredTime { get; set; }

	/// <summary>
	/// Subclass override: can <paramref name="other"/> merge into this slot? Default
	/// false (weapons never stack). MoneyPickup / future AmmoPickup override.
	/// </summary>
	public virtual bool CanStackWith( BaseItem other ) => false;

	/// <summary>
	/// Right-click action from the inventory UI. Weapons equip; money is a no-op
	/// (already spendable); ammo would load into the held weapon, etc.
	/// </summary>
	public abstract void OnUse( GameObject player );
}
