using Sandbox;

namespace Sandbox.Systems.Inventory;

/// <summary>
/// Holds the rarity → color palette used by the inventory UI to draw each slot's
/// always-on rarity glow. Lives on the player root so designers tweak the whole
/// palette in one place — items themselves only carry a <see cref="Rarity"/> enum,
/// never a color.
/// </summary>
public sealed class RarityConfig : Component
{
	[Property] public Color CommonColor { get; set; } = Color.White;
	[Property] public Color UncommonColor { get; set; } = new Color( 0.46f, 0.86f, 0.49f );
	[Property] public Color RareColor { get; set; } = new Color( 0.30f, 0.58f, 1.0f );
	[Property] public Color EpicColor { get; set; } = new Color( 0.69f, 0.32f, 0.87f );
	[Property] public Color LegendaryColor { get; set; } = new Color( 1.0f, 0.65f, 0.0f );

	public Color GetColor( Rarity rarity ) => rarity switch
	{
		Rarity.Uncommon => UncommonColor,
		Rarity.Rare => RareColor,
		Rarity.Epic => EpicColor,
		Rarity.Legendary => LegendaryColor,
		_ => CommonColor,
	};
}
