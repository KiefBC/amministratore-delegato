using Sandbox;

/// <summary>
/// Implement on any component (bounty payout, kill feed, achievements, wave spawner,
/// score tracker) that wants to react when a unit dies. Broadcast via
/// Scene.RunEvent&lt;IUnitDiedListener&gt; by <see cref="UnitComponent"/> from <c>Die()</c>.
/// </summary>
public interface IUnitDiedListener
{
	/// <summary>
	/// Fired once per unit death. <paramref name="killer"/> is the GameObject root of
	/// the attacker that landed the killing blow, or null if the unit died from a
	/// non-tracked source (e.g. inspector debug button, environmental damage).
	/// </summary>
	void OnUnitDied( UnitComponent unit, GameObject killer );
}
