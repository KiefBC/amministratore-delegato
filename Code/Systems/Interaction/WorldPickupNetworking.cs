using Sandbox;

namespace Sandbox.Systems.Interaction;

public static class WorldPickupNetworking
{
	public static void Configure( GameObject gameObject )
	{
		if ( !gameObject.IsValid() ) return;

		gameObject.NetworkMode = NetworkMode.Object;
		gameObject.Network.SetOwnerTransfer( OwnerTransfer.Fixed );
	}

	public static bool TrySpawnOrRefresh( GameObject gameObject, string label, ref bool warningLogged )
	{
		if ( !gameObject.IsValid() ) return false;
		Configure( gameObject );

		if ( !Sandbox.Networking.IsHost ) return true;
		if ( !Sandbox.Networking.IsActive ) return false;

		if ( gameObject.Network.Active )
		{
			gameObject.Network.Refresh();
			return true;
		}

		if ( gameObject.NetworkSpawn() ) return true;

		if ( !warningLogged )
		{
			warningLogged = true;
			Log.Warning( $"[PickupNetwork] Failed to NetworkSpawn {label}; object={gameObject.Name}." );
		}

		return false;
	}
}
