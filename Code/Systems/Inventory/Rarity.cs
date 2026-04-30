/// <summary>
/// Item rarity tier. Set per-prefab on each <see cref="BaseItem"/>; the inventory
/// UI looks up the matching color in <see cref="RarityConfig"/> to draw the slot's
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
