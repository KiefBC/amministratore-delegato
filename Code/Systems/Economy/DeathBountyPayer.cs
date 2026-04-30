using Sandbox;

/// <summary>
/// Listens for unit deaths and pays the killing player the dead unit's
/// <see cref="UnitComponent.Bounty"/> via <see cref="EconomySystem"/>.
///
/// Place one on a persistent scene GameObject (e.g. a Systems object). Scene-wide
/// — placement doesn't matter, <c>Scene.RunEvent</c> finds it by component type.
/// </summary>
public sealed class DeathBountyPayer : Component, IUnitDiedListener
{
	public void OnUnitDied( UnitComponent unit, GameObject killer )
	{
		if ( unit is null ) return;
		if ( unit.Bounty <= 0 ) return;
		if ( killer is null ) return;

		var killerUnit = killer.Components.GetInDescendantsOrSelf<UnitComponent>();
		if ( killerUnit is null ) return;
		if ( killerUnit.Team != TeamType.Player ) return;

		EconomySystem.Current?.Add( unit.Bounty );
		Log.Info( $"{killerUnit.Name} earned ${unit.Bounty} for killing {unit.Name}" );
	}
}
