using Sandbox;

namespace Sandbox.Systems.Economy;

/// <summary>
/// Scene-scoped economy service. It does not own player money; each player's
/// physical money stack is the authoritative cash balance.
/// </summary>
public sealed class EconomySystem : GameObjectSystem<EconomySystem>
{
	public EconomySystem( Scene scene ) : base( scene )
	{
		Listen( Stage.StartFixedUpdate, 0, OnTick, nameof( OnTick ) );
	}

	private void OnTick()
	{
		// Future: walk owned businesses, sum their per-owner income, call Add( player, total ).
		// Empty today — the seam exists, the implementation does not.
	}

	/// <summary>
	/// Add money to one player's wallet. Only the host can mint income/rewards.
	/// </summary>
	public bool Add( GameObject player, int amount )
	{
		if ( amount <= 0 ) return false;
		if ( !Sandbox.Networking.IsHost ) return false;

		var backpack = player?.Components.GetInDescendantsOrSelf<Backpack>();
		return backpack.IsValid() && backpack.AddMoney( amount );
	}

	/// <summary>
	/// Try to spend. Host calls return the real result. Non-host calls are forwarded
	/// asynchronously and return false until the synced balance updates.
	/// </summary>
	public bool TrySpend( GameObject player, int amount )
	{
		if ( amount <= 0 ) return false;
		if ( !Sandbox.Networking.IsHost )
		{
			GameNetworkRpc.RequestSpendMoney( player, amount );
			return false;
		}

		var backpack = player?.Components.GetInDescendantsOrSelf<Backpack>();
		return backpack.IsValid() && backpack.TrySpend( amount );
	}
}
