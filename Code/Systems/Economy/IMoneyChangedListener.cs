/// <summary>
/// Implement on any component (HUD, business, achievement tracker) that wants
/// to react to money changes. Broadcast via Scene.RunEvent&lt;IMoneyChangedListener&gt;
/// by EconomySystem after Money mutates.
/// </summary>
public interface IMoneyChangedListener
{
	void OnMoneyChanged( int newAmount, int delta );
}
