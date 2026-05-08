namespace Sandbox.Systems.Inventory;

/// <summary>
/// Item rarity tier. Set on <see cref="ItemDefinition"/> assets; the inventory UI
/// looks up the matching color in <see cref="RarityConfig"/> to draw the slot's
/// always-on rarity glow.
/// </summary>
public enum Rarity
{
	Common,
	Uncommon,
	Rare,
	Epic,
	Legendary,
}
