/// <summary>
/// Implement on any component that wants to react to damage events scene-wide
/// (HUD hit-markers, sound, kill feed, future score). Broadcast via
/// Scene.RunEvent&lt;IDamagedListener&gt; by CombatSystem after damage is applied.
/// </summary>
public interface IDamagedListener
{
	void OnDamaged( Component.IDamageable target, DamageInfo info, bool wasKill );
}
