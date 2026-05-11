using Sandbox;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Sandbox.Systems.Cloud;

public static class CloudFinanceClient
{
	public static async Task RequestAsync( GameObject player, CloudFinanceAction action, int amount, FinanceAccountSource account, GameObject recipient = null, CancellationToken cancellationToken = default )
	{
		if ( !player.IsValid() ) return;

		var config = CloudProgressionSystem.Current?.Config;
		if ( !config.IsValid() || !config.HasBackend )
		{
			NotificationSystem.Current?.Notify( NotificationKind.Warning, "Cloud Offline", "Cloud finance is not configured.", 3f );
			return;
		}

		try
		{
			var token = await Sandbox.Services.Auth.GetToken( config.ResolvedAuthServiceName(), cancellationToken );
			if ( cancellationToken.IsCancellationRequested || !player.IsValid() ) return;

			GameNetworkRpc.RequestCloudFinanceAction( player, (int)action, amount, (int)account, recipient, token );
		}
		catch ( OperationCanceledException ) when ( cancellationToken.IsCancellationRequested )
		{
		}
		catch ( Exception e )
		{
			Log.Warning( $"[Cloud] Failed to request finance auth token: {e.Message}" );
			NotificationSystem.Current?.Notify( NotificationKind.BadNews, "Cloud Auth Failed", "Could not authenticate the finance request.", 3f );
		}
	}
}
