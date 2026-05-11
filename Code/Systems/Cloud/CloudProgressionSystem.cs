using Sandbox;
using Sandbox.Network;
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Sandbox.Systems.Cloud;

public sealed class CloudProgressionSystem : GameObjectSystem<CloudProgressionSystem>
{
	private readonly HashSet<string> _pendingFinanceActions = new();
	private readonly object _pendingFinanceActionsLock = new();

	public CloudProgressionConfig Config => Scene.GetAllComponents<CloudProgressionConfig>().FirstOrDefault( x => x.IsValid() );

	public CloudProgressionSystem( Scene scene ) : base( scene )
	{
	}

	public async Task LoadPlayerAsync( GameObject player, string authToken, Connection caller, CancellationToken cancellationToken )
	{
		if ( !Sandbox.Networking.IsHost ) return;
		if ( !player.IsValid() ) return;

		var config = Config;
		if ( !config.IsValid() || !config.HasBackend ) return;
		if ( string.IsNullOrWhiteSpace( authToken ) )
		{
			NotifyPlayer( player, NotificationKind.BadNews, "Cloud Load Failed", "Missing s&box auth token.", 3f );
			return;
		}

		try
		{
			var response = await PostAsync( config, config.LoadPlayerEndpoint, BuildPlayerPayload( config, player, caller ), authToken, cancellationToken );
			if ( cancellationToken.IsCancellationRequested || !player.IsValid() ) return;

			var state = ParseCloudStateResponse( response );
			if ( !state.Success )
			{
				NotifyPlayer( player, NotificationKind.BadNews, "Cloud Load Failed", state.MessageOrDefault( "The backend rejected the load request." ), 3f );
				return;
			}

			ApplyCloudState( player, state, "player load" );
			NotifyPlayer( player, NotificationKind.Success, "Cloud Loaded", "Persistent progress loaded.", 2.5f );
		}
		catch ( OperationCanceledException ) when ( cancellationToken.IsCancellationRequested )
		{
		}
		catch ( Exception e )
		{
			Log.Warning( $"[Cloud] Player load failed: {e.Message}" );
			NotifyPlayer( player, NotificationKind.BadNews, "Cloud Load Failed", "Persistent progress could not be loaded.", 3f );
		}
	}

	public async Task CompleteJobAsync( GameObject player, CloudJobWorkstation workstation, string authToken, Connection caller, CancellationToken cancellationToken )
	{
		if ( !Sandbox.Networking.IsHost ) return;
		if ( !player.IsValid() || !workstation.IsValid() ) return;

		var config = Config;
		if ( !config.IsValid() || !config.HasBackend )
		{
			ApplyLocalFallbackIfAllowed( config, player );
			return;
		}

		if ( string.IsNullOrWhiteSpace( authToken ) )
		{
			NotifyPlayer( player, NotificationKind.BadNews, "Job Failed", "Missing s&box auth token.", 3f );
			return;
		}

		try
		{
			var payload = BuildPlayerPayload( config, player, caller );
			payload["job_id"] = workstation.JobId;
			payload["claim_id"] = ClaimId( config, player, workstation );

			var response = await PostAsync( config, config.CompleteJobEndpoint, payload, authToken, cancellationToken );
			if ( cancellationToken.IsCancellationRequested || !player.IsValid() || !workstation.IsValid() ) return;

			var state = ParseCloudStateResponse( response );
			if ( !state.Success )
			{
				NotifyPlayer( player, NotificationKind.BadNews, "Job Failed", state.MessageOrDefault( "The backend rejected the job." ), 3f );
				return;
			}

			ApplyCloudState( player, state, "job completion" );
			var reward = state.RewardMoney.GetValueOrDefault();
			var message = reward > 0 ? $"Deposited ${reward:N0} to bank." : state.MessageOrDefault( "Job completed." );
			NotifyPlayer( player, NotificationKind.Success, "Job Complete", message, 3f );
		}
		catch ( OperationCanceledException ) when ( cancellationToken.IsCancellationRequested )
		{
		}
		catch ( Exception e )
		{
			Log.Warning( $"[Cloud] Job completion failed: {e.Message}" );
			NotifyPlayer( player, NotificationKind.BadNews, "Job Failed", "The backend job request failed.", 3f );
		}
	}

	public async Task SubmitFinanceActionAsync( GameObject player, CloudFinanceAction action, int amount, FinanceAccountSource account, GameObject recipient, string authToken, Connection caller, CancellationToken cancellationToken )
	{
		if ( !Sandbox.Networking.IsHost ) return;
		if ( !player.IsValid() ) return;

		var config = Config;
		if ( !config.IsValid() || !config.HasBackend )
		{
			NotifyPlayer( player, NotificationKind.Warning, "Cloud Offline", "Cloud finance is not configured.", 3f );
			return;
		}

		if ( amount <= 0 )
		{
			NotifyPlayer( player, NotificationKind.Warning, "Finance", "Enter a valid amount.", 2.5f );
			return;
		}

		if ( string.IsNullOrWhiteSpace( authToken ) )
		{
			NotifyPlayer( player, NotificationKind.BadNews, "Finance Failed", "Missing s&box auth token.", 3f );
			return;
		}

		var finance = player.Components.GetInDescendantsOrSelf<PlayerFinanceComponent>();
		var backpack = player.Components.GetInDescendantsOrSelf<Backpack>();
		if ( !finance.IsValid() || !backpack.IsValid() ) return;

		var expectedWalletDelta = ExpectedWalletDelta( action, amount, account, finance );
		if ( expectedWalletDelta < 0 && backpack.Wallet < -expectedWalletDelta )
		{
			NotifyPlayer( player, NotificationKind.BadNews, "Not Enough Cash", $"Need ${-expectedWalletDelta:N0} cash.", 3f );
			return;
		}

		if ( expectedWalletDelta > 0 && !backpack.CanAddMoney( expectedWalletDelta ) )
		{
			NotifyPlayer( player, NotificationKind.BadNews, "No Cash Space", $"Make room for ${expectedWalletDelta:N0} cash.", 3f );
			return;
		}

		var pendingKey = FinanceActionKey( player, caller );
		lock ( _pendingFinanceActionsLock )
		{
			if ( !_pendingFinanceActions.Add( pendingKey ) )
			{
				NotifyPlayer( player, NotificationKind.Warning, "Finance Pending", "Wait for the current finance action to finish.", 2.5f );
				return;
			}
		}

		try
		{
			var payload = BuildPlayerPayload( config, player, caller );
			payload["action"] = ActionId( action );
			payload["amount"] = amount;
			payload["source_account"] = SourceAccountId( action, account );
			payload["destination_account"] = DestinationAccountId( action, account );

			if ( action == CloudFinanceAction.Transfer )
			{
				if ( !recipient.IsValid() || recipient == player )
				{
					NotifyPlayer( player, NotificationKind.Warning, "Transfer", "Select a valid recipient.", 2.5f );
					return;
				}

				var recipientOwner = recipient.Network.Owner;
				if ( recipientOwner is null )
				{
					NotifyPlayer( player, NotificationKind.Warning, "Transfer", "Recipient is not available.", 2.5f );
					return;
				}

				payload["recipient_steam_id"] = recipientOwner.SteamId.ToString();
			}

			var response = await PostAsync( config, config.FinanceActionEndpoint, payload, authToken, cancellationToken );
			if ( cancellationToken.IsCancellationRequested || !player.IsValid() ) return;

			var state = ParseCloudStateResponse( response );
			if ( !state.Success )
			{
				NotifyPlayer( player, NotificationKind.BadNews, "Finance Failed", state.MessageOrDefault( "The backend rejected the finance action." ), 3f );
				return;
			}

			ApplyWalletDelta( player, state.WalletDelta.GetValueOrDefault() );
			ApplyCloudState( player, state, $"finance {ActionId( action )}" );

			if ( action == CloudFinanceAction.Transfer && recipient.IsValid() && state.RecipientBankBalance.HasValue )
			{
				var recipientFinance = recipient.Components.GetInDescendantsOrSelf<PlayerFinanceComponent>();
				if ( recipientFinance.IsValid() ) recipientFinance.ApplyCloudFinanceSnapshot( state.RecipientBankBalance.Value, null, null, "finance transfer received" );
			}

			NotifyPlayer( player, NotificationKind.Success, "Finance", state.MessageOrDefault( "Finance action complete." ), 3f );
		}
		catch ( OperationCanceledException ) when ( cancellationToken.IsCancellationRequested )
		{
		}
		catch ( Exception e )
		{
			Log.Warning( $"[Cloud] Finance action failed: {e.Message}" );
			NotifyPlayer( player, NotificationKind.BadNews, "Finance Failed", "The backend finance request failed.", 3f );
		}
		finally
		{
			lock ( _pendingFinanceActionsLock )
			{
				_pendingFinanceActions.Remove( pendingKey );
			}
		}
	}

	private void ApplyLocalFallbackIfAllowed( CloudProgressionConfig config, GameObject player )
	{
		if ( !config.IsValid() || !config.AllowLocalDevFallback )
		{
			NotifyPlayer( player, NotificationKind.Warning, "Cloud Offline", "No cloud backend is configured for this scene.", 3f );
			return;
		}

		var finance = player.Components.GetInDescendantsOrSelf<PlayerFinanceComponent>();
		var progress = player.Components.GetInDescendantsOrSelf<CloudPlayerProgressComponent>();
		if ( !finance.IsValid() || !progress.IsValid() ) return;

		var rewardMoney = int.Max( 0, config.LocalFallbackJobRewardMoney );
		var rewardXp = int.Max( 0, config.LocalFallbackJobRewardXp );
		finance.AddMoney( FinanceAccountSource.Bank, rewardMoney );
		progress.ApplyJobSnapshot( progress.JobXp + rewardXp, progress.JobCompletions + 1, UnixNow() );
		NotifyPlayer( player, NotificationKind.Success, "Local Job Complete", $"Dev fallback added ${rewardMoney:N0}.", 3f );
	}

	private static Dictionary<string, object> BuildPlayerPayload( CloudProgressionConfig config, GameObject player, Connection caller )
	{
		var owner = player.Network.Owner;
		var connection = caller ?? owner;

		return new Dictionary<string, object>
		{
			["session_id"] = config.ResolvedSessionId(),
			["steam_id"] = connection?.SteamId.ToString() ?? "",
			["display_name"] = connection?.DisplayName ?? DisplayNameFor( player ),
			["game_ident"] = "amministratore_delegato",
		};
	}

	private static async Task<string> PostAsync( CloudProgressionConfig config, string endpoint, Dictionary<string, object> payload, string authToken, CancellationToken cancellationToken )
	{
		var url = config.EndpointUrl( endpoint );
		if ( string.IsNullOrWhiteSpace( url ) ) throw new InvalidOperationException( "Cloud backend endpoint is not configured." );

		var headers = new Dictionary<string, string>
		{
			["Authorization"] = $"Bearer {authToken}",
			["X-Sbox-Auth-Token"] = authToken,
			["X-Sbox-Auth-Service"] = config.ResolvedAuthServiceName(),
		};

		var response = await Sandbox.Http.RequestAsync( url, "POST", Sandbox.Http.CreateJsonContent( payload ), headers, cancellationToken );
		var body = await response.Content.ReadAsStringAsync( cancellationToken );
		if ( response.IsSuccessStatusCode || !string.IsNullOrWhiteSpace( body ) ) return body;

		throw new InvalidOperationException( $"Cloud backend returned {(int)response.StatusCode} {response.ReasonPhrase}." );
	}

	private static void ApplyCloudState( GameObject player, CloudStateResponse state, string reason )
	{
		var finance = player.Components.GetInDescendantsOrSelf<PlayerFinanceComponent>();
		if ( finance.IsValid() )
		{
			if ( state.BankBalance.HasValue || state.DebtBalance.HasValue || state.NextDebtAccrualAtUnix.HasValue ) finance.ApplyCloudFinanceSnapshot( state.BankBalance, state.DebtBalance, state.NextDebtAccrualAtUnix, reason );
			else if ( state.RewardMoney.GetValueOrDefault() > 0 ) finance.AddMoney( FinanceAccountSource.Bank, state.RewardMoney.Value );
		}

		var progress = player.Components.GetInDescendantsOrSelf<CloudPlayerProgressComponent>();
		if ( progress.IsValid() )
		{
			var jobXp = state.JobXp ?? progress.JobXp + state.RewardXp.GetValueOrDefault();
			var completions = state.JobCompletions ?? progress.JobCompletions + (state.RewardMoney.HasValue || state.RewardXp.HasValue ? 1 : 0);
			var lastJobAt = state.LastJobAtUnix ?? progress.LastJobAtUnix;
			progress.ApplyJobSnapshot( jobXp, completions, lastJobAt );
		}

		GameLogSystem.Current?.Info( "cloud", "Cloud state applied", player, data: GameLogSystem.Fields(
			("reason", reason),
			("bankBalance", state.BankBalance),
			("debtBalance", state.DebtBalance),
			("nextDebtAccrualAtUnix", state.NextDebtAccrualAtUnix),
			("jobXp", state.JobXp),
			("jobCompletions", state.JobCompletions),
			("rewardMoney", state.RewardMoney),
			("rewardXp", state.RewardXp) ) );
	}

	private static CloudStateResponse ParseCloudStateResponse( string json )
	{
		if ( string.IsNullOrWhiteSpace( json ) ) return CloudStateResponse.Fail( "Empty backend response." );

		JsonDocument document;
		try
		{
			document = JsonDocument.Parse( json );
		}
		catch ( JsonException )
		{
			return CloudStateResponse.Fail( $"Invalid backend response: {json}" );
		}

		using ( document )
		{
		var root = document.RootElement;
		var stateRoot = NestedObject( root, "player", "state", "data" ) ?? root;

		var success = ReadBool( root, "ok", "success" ) ?? true;
		var message = ReadString( root, "message", "error" ) ?? ReadString( stateRoot, "message", "error" ) ?? "";

		return new CloudStateResponse
		{
			Success = success,
			Message = message,
			BankBalance = ReadInt( stateRoot, "bank_balance", "bankBalance", "BankBalance" ),
			DebtBalance = ReadInt( stateRoot, "debt_balance", "debtBalance", "DebtBalance" ),
			NextDebtAccrualAtUnix = ReadLong( stateRoot, "next_debt_accrual_at", "nextDebtAccrualAt", "NextDebtAccrualAtUnix" ),
			JobXp = ReadInt( stateRoot, "job_xp", "jobXp", "JobXp" ),
			JobCompletions = ReadInt( stateRoot, "job_completions", "jobCompletions", "JobCompletions" ),
			LastJobAtUnix = ReadLong( stateRoot, "last_job_at", "lastJobAt", "LastJobAtUnix" ),
			RewardMoney = ReadInt( root, "reward_money", "rewardMoney", "RewardMoney" ),
			RewardXp = ReadInt( root, "reward_xp", "rewardXp", "RewardXp" ),
			WalletDelta = ReadInt( root, "wallet_delta", "walletDelta", "WalletDelta" ),
			RecipientSteamId = ReadString( root, "recipient_steam_id", "recipientSteamId", "RecipientSteamId" ) ?? "",
			RecipientBankBalance = ReadInt( root, "recipient_bank_balance", "recipientBankBalance", "RecipientBankBalance" ),
		};
		}
	}

	private static JsonElement? NestedObject( JsonElement root, params string[] names )
	{
		foreach ( var name in names )
		{
			if ( root.TryGetProperty( name, out var element ) && element.ValueKind == JsonValueKind.Object ) return element;
		}

		return null;
	}

	private static bool? ReadBool( JsonElement root, params string[] names )
	{
		foreach ( var name in names )
		{
			if ( !root.TryGetProperty( name, out var element ) ) continue;
			if ( element.ValueKind == JsonValueKind.True ) return true;
			if ( element.ValueKind == JsonValueKind.False ) return false;
			if ( element.ValueKind == JsonValueKind.String && bool.TryParse( element.GetString(), out var parsed ) ) return parsed;
		}

		return null;
	}

	private static int? ReadInt( JsonElement root, params string[] names )
	{
		foreach ( var name in names )
		{
			if ( !root.TryGetProperty( name, out var element ) ) continue;
			if ( element.TryGetInt32( out var value ) ) return value;
			if ( element.ValueKind == JsonValueKind.String && int.TryParse( element.GetString(), out var parsed ) ) return parsed;
		}

		return null;
	}

	private static long? ReadLong( JsonElement root, params string[] names )
	{
		foreach ( var name in names )
		{
			if ( !root.TryGetProperty( name, out var element ) ) continue;
			if ( element.TryGetInt64( out var value ) ) return value;
			if ( element.ValueKind == JsonValueKind.String && long.TryParse( element.GetString(), out var parsed ) ) return parsed;
		}

		return null;
	}

	private static string ReadString( JsonElement root, params string[] names )
	{
		foreach ( var name in names )
		{
			if ( !root.TryGetProperty( name, out var element ) ) continue;
			if ( element.ValueKind == JsonValueKind.String ) return element.GetString();
		}

		return null;
	}

	private static void ApplyWalletDelta( GameObject player, int walletDelta )
	{
		if ( walletDelta == 0 ) return;

		var backpack = player.Components.GetInDescendantsOrSelf<Backpack>();
		if ( !backpack.IsValid() ) return;

		var applied = walletDelta > 0
			? backpack.AddMoney( walletDelta )
			: backpack.TrySpend( -walletDelta );

		if ( applied ) return;

		Log.Warning( $"[Cloud] Could not apply wallet delta after confirmed finance action; player={DisplayNameFor( player )}; walletDelta=${walletDelta:N0}." );
		NotifyPlayer( player, NotificationKind.BadNews, "Wallet Sync Failed", "Cloud finance changed, but local cash could not be updated.", 4f );
	}

	private static int ExpectedWalletDelta( CloudFinanceAction action, int amount, FinanceAccountSource account, PlayerFinanceComponent finance )
	{
		return action switch
		{
			CloudFinanceAction.Deposit => -amount,
			CloudFinanceAction.Withdraw => amount,
			CloudFinanceAction.TakeLoan when account == FinanceAccountSource.Wallet => amount,
			CloudFinanceAction.RepayDebt when account == FinanceAccountSource.Wallet => -int.Min( amount, finance?.DebtBalance ?? amount ),
			CloudFinanceAction.Transfer when account == FinanceAccountSource.Wallet => -amount,
			_ => 0,
		};
	}

	private static string ActionId( CloudFinanceAction action )
	{
		return action switch
		{
			CloudFinanceAction.Deposit => "deposit",
			CloudFinanceAction.Withdraw => "withdraw",
			CloudFinanceAction.TakeLoan => "take_loan",
			CloudFinanceAction.RepayDebt => "repay_debt",
			CloudFinanceAction.Transfer => "transfer",
			_ => "unknown",
		};
	}

	private static string SourceAccountId( CloudFinanceAction action, FinanceAccountSource account )
	{
		return action is CloudFinanceAction.RepayDebt or CloudFinanceAction.Transfer ? AccountId( account ) : "";
	}

	private static string DestinationAccountId( CloudFinanceAction action, FinanceAccountSource account )
	{
		return action == CloudFinanceAction.TakeLoan ? AccountId( account ) : "";
	}

	private static string AccountId( FinanceAccountSource account )
	{
		return account == FinanceAccountSource.Bank ? "bank" : "wallet";
	}

	private static string FinanceActionKey( GameObject player, Connection caller )
	{
		var owner = caller ?? player.Network.Owner;
		return owner is not null ? owner.SteamId.ToString() : player.GetHashCode().ToString();
	}

	private static string ClaimId( CloudProgressionConfig config, GameObject player, CloudJobWorkstation workstation )
	{
		var owner = player.Network.Owner;
		var steamId = owner?.SteamId.ToString() ?? "unknown";
		return $"{config.ResolvedSessionId()}:{steamId}:{workstation.JobId}:{Guid.NewGuid():N}";
	}

	private static long UnixNow()
	{
		return DateTimeOffset.UtcNow.ToUnixTimeSeconds();
	}

	private static string DisplayNameFor( GameObject player )
	{
		return player.IsValid() && !string.IsNullOrWhiteSpace( player.Name ) ? player.Name : "Player";
	}

	private static void NotifyPlayer( GameObject player, NotificationKind kind, string title, string message, float shownDuration )
	{
		GameNetworkRpc.BroadcastPlayerNotification( player, (int)kind, title, message, shownDuration );
	}

	private sealed class CloudStateResponse
	{
		public bool Success { get; set; } = true;
		public string Message { get; set; } = "";
		public int? BankBalance { get; set; }
		public int? DebtBalance { get; set; }
		public long? NextDebtAccrualAtUnix { get; set; }
		public int? JobXp { get; set; }
		public int? JobCompletions { get; set; }
		public long? LastJobAtUnix { get; set; }
		public int? RewardMoney { get; set; }
		public int? RewardXp { get; set; }
		public int? WalletDelta { get; set; }
		public string RecipientSteamId { get; set; } = "";
		public int? RecipientBankBalance { get; set; }

		public string MessageOrDefault( string fallback )
		{
			return string.IsNullOrWhiteSpace( Message ) ? fallback : Message;
		}

		public static CloudStateResponse Fail( string message )
		{
			return new CloudStateResponse { Success = false, Message = message };
		}
	}
}
