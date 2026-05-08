using Sandbox;

/// <summary>
/// RPC entry points for gameplay systems. Keep network transport here so scene
/// systems can stay focused on gameplay rules and local scene queries.
/// </summary>
public static class GameNetworkRpc
{
	[Rpc.Broadcast]
	public static void BroadcastDamaged( GameObject targetGo, GameObject attacker, GameObject weapon, float damage, Vector3 position, Vector3 origin, bool wasKill )
	{
		if ( !CallerIsHost() ) return;

		var info = new DamageInfo
		{
			Attacker = attacker,
			Weapon = weapon,
			Damage = damage,
			Position = position,
			Origin = origin,
		};

		CombatSystem.Current?.NotifyDamaged( targetGo, info, wasKill );
	}

	[Rpc.Host]
	public static void RequestInteract( GameObject targetGo, GameObject player )
	{
		if ( !CallerOwns( player ) ) return;

		InteractionSystem.Current?.TryInteractOnHost( targetGo, player );
	}

	[Rpc.Host]
	public static void RequestUseInventorySlot( GameObject player, int slot )
	{
		if ( !CallerOwns( player ) ) return;

		BackpackFor( player )?.TryUseSlot( slot );
	}

	[Rpc.Host]
	public static void RequestMoveInventorySlot( GameObject player, int fromSlot, int toSlot )
	{
		if ( !CallerOwns( player ) ) return;

		BackpackFor( player )?.TryMoveSlot( fromSlot, toSlot );
	}

	[Rpc.Host]
	public static void RequestDropInventorySlot( GameObject player, int slot )
	{
		if ( !CallerOwns( player ) ) return;

		BackpackFor( player )?.TryDropSlot( slot );
	}

	[Rpc.Host]
	public static void RequestSortInventory( GameObject player, int sortMode )
	{
		if ( !CallerOwns( player ) ) return;
		if ( !System.Enum.IsDefined( typeof( Backpack.SortMode ), sortMode ) ) return;

		BackpackFor( player )?.TrySort( (Backpack.SortMode)sortMode );
	}

	[Rpc.Host]
	public static void RequestSpendMoney( GameObject player, int amount )
	{
		if ( !CallerOwns( player ) ) return;

		EconomySystem.Current?.TrySpend( player, amount );
	}

	[Rpc.Host]
	public static void RequestDepositMoney( GameObject player, int amount )
	{
		if ( !CallerOwns( player ) ) return;

		FinanceFor( player )?.TryDeposit( amount );
	}

	[Rpc.Host]
	public static void RequestWithdrawMoney( GameObject player, int amount )
	{
		if ( !CallerOwns( player ) ) return;

		FinanceFor( player )?.TryWithdraw( amount );
	}

	[Rpc.Host]
	public static void RequestTransferMoney( GameObject player, GameObject recipient, int amount, int sourceAccount )
	{
		if ( !CallerOwns( player ) ) return;
		if ( !recipient.IsValid() || recipient == player ) return;
		if ( !System.Enum.IsDefined( typeof( FinanceAccountSource ), sourceAccount ) ) return;

		var finance = FinanceFor( player );
		var recipientFinance = FinanceFor( recipient );
		if ( !finance.IsValid() || !recipientFinance.IsValid() ) return;
		if ( !finance.TrySpend( (FinanceAccountSource)sourceAccount, amount ) ) return;

		recipientFinance.AddMoney( FinanceAccountSource.Bank, amount );
	}

	[Rpc.Host]
	public static void RequestBuyBusiness( GameObject player, string businessId )
	{
		if ( !CallerOwns( player ) ) return;

		FinanceFor( player )?.TryBuyBusiness( businessId );
	}

	[Rpc.Host]
	public static void RequestBuyStock( GameObject player, string symbol, int amount, int sourceAccount )
	{
		if ( !CallerOwns( player ) ) return;
		if ( !System.Enum.IsDefined( typeof( FinanceAccountSource ), sourceAccount ) ) return;

		var offer = FinanceCatalog.Stock( symbol );
		if ( offer is null ) return;

		var finance = FinanceFor( player );
		var backpack = BackpackFor( player );
		if ( !finance.IsValid() || !backpack.IsValid() ) return;

		var source = (FinanceAccountSource)sourceAccount;
		var price = MarketDataSystem.Current?.StockPrice( offer.Symbol ) ?? offer.FallbackPrice;
		if ( amount <= 0 || price <= 0m )
		{
			NotifyTerminal( player, NotificationKind.Warning, "Stock Order", "Enter a valid dollar amount.", 2.5f );
			return;
		}

		var shares = decimal.ToInt32( decimal.Floor( amount / price ) );
		if ( shares <= 0 )
		{
			NotifyTerminal( player, NotificationKind.Warning, "Stock Order", $"Enter at least ${decimal.ToInt32( decimal.Ceiling( price ) ):N0} to buy 1 share of {offer.Symbol}.", 3f );
			return;
		}

		var cost = decimal.ToInt32( decimal.Ceiling( shares * price ) );
		var fee = StockTradeFee( cost );
		if ( cost > int.MaxValue - fee )
		{
			NotifyTerminal( player, NotificationKind.BadNews, "Stock Order Failed", "The order total is too large.", 3f );
			return;
		}

		var total = cost + fee;
		var balance = source == FinanceAccountSource.Bank ? finance.BankBalance : backpack.Wallet;
		if ( balance < total )
		{
			NotifyTerminal( player, NotificationKind.BadNews, "Insufficient Funds", $"Need ${total:N0} in {AccountDisplayName( source )} to buy {shares:N0} {offer.Symbol} share{Plural( shares )} (${cost:N0} + ${fee:N0} fee).", 3.5f );
			return;
		}

		if ( !finance.TryBuyStockShares( offer.Symbol, shares, price, source, fee ) )
		{
			NotifyTerminal( player, NotificationKind.BadNews, "Stock Order Failed", "The order could not be completed.", 3f );
			return;
		}

		AddStockTradeFeeToVault( player, offer.Symbol, cost, fee, "buy" );
		NotifyTerminal( player, NotificationKind.Success, "Stock Purchased", $"Bought {shares:N0} {offer.Symbol} share{Plural( shares )} for ${cost:N0}. Fee ${fee:N0}; total ${total:N0}.", 3f );
	}

	[Rpc.Host]
	public static void RequestBuyStockShares( GameObject player, string symbol, int shares )
	{
		if ( !CallerOwns( player ) )
		{
			Log.Warning( $"[StockTerminal] Buy RPC rejected ownership; player={PlayerLogName( player )}; symbol={symbol}; shares={shares:N0}." );
			return;
		}

		var offer = FinanceCatalog.Stock( symbol );
		if ( offer is null )
		{
			Log.Warning( $"[StockTerminal] Buy RPC rejected unknown stock; player={PlayerLogName( player )}; symbol={symbol}; shares={shares:N0}." );
			return;
		}

		var finance = FinanceFor( player );
		var backpack = BackpackFor( player );
		if ( !finance.IsValid() || !backpack.IsValid() )
		{
			Log.Warning( $"[StockTerminal] Buy RPC rejected missing components; player={PlayerLogName( player )}; symbol={offer.Symbol}; shares={shares:N0}; financeValid={finance.IsValid()}; backpackValid={backpack.IsValid()}." );
			return;
		}

		var price = MarketDataSystem.Current?.StockPrice( offer.Symbol ) ?? offer.FallbackPrice;
		if ( shares <= 0 || price <= 0m )
		{
			Log.Warning( $"[StockTerminal] Buy RPC rejected invalid amount; player={PlayerLogName( player )}; symbol={offer.Symbol}; shares={shares:N0}; price=${PriceLog( price )}." );
			NotifyTerminal( player, NotificationKind.Warning, "Stock Order", "Enter a valid share amount.", 2.5f );
			return;
		}

		var cost = decimal.ToInt32( decimal.Ceiling( shares * price ) );
		var fee = StockTradeFee( cost );
		if ( cost > int.MaxValue - fee )
		{
			Log.Warning( $"[StockTerminal] Buy RPC rejected oversized total; player={PlayerLogName( player )}; symbol={offer.Symbol}; shares={shares:N0}; cost=${cost:N0}; fee=${fee:N0}." );
			NotifyTerminal( player, NotificationKind.BadNews, "Stock Order Failed", "The order total is too large.", 3f );
			return;
		}

		var total = cost + fee;
		Log.Info( $"[StockTerminal] Buy RPC received; player={PlayerLogName( player )}; symbol={offer.Symbol}; shares={shares:N0}; price=${PriceLog( price )}; cost=${cost:N0}; fee=${fee:N0}; total=${total:N0}; wallet=${backpack.Wallet:N0}." );
		if ( backpack.Wallet < total )
		{
			Log.Warning( $"[StockTerminal] Buy RPC rejected insufficient funds; player={PlayerLogName( player )}; symbol={offer.Symbol}; shares={shares:N0}; cost=${cost:N0}; fee=${fee:N0}; total=${total:N0}; wallet=${backpack.Wallet:N0}." );
			NotifyTerminal( player, NotificationKind.BadNews, "Insufficient Funds", $"Need ${total:N0} cash to buy {shares:N0} {offer.Symbol} share{Plural( shares )} (${cost:N0} + ${fee:N0} fee).", 3.5f );
			return;
		}

		if ( !finance.TryBuyStockShares( offer.Symbol, shares, price, FinanceAccountSource.Wallet, fee ) )
		{
			Log.Warning( $"[StockTerminal] Buy RPC failed during mutation; player={PlayerLogName( player )}; symbol={offer.Symbol}; shares={shares:N0}; cost=${cost:N0}; fee=${fee:N0}; total=${total:N0}; wallet=${backpack.Wallet:N0}." );
			NotifyTerminal( player, NotificationKind.BadNews, "Stock Order Failed", "The order could not be completed.", 3f );
			return;
		}

		AddStockTradeFeeToVault( player, offer.Symbol, cost, fee, "buy" );
		Log.Info( $"[StockTerminal] Buy RPC completed; player={PlayerLogName( player )}; symbol={offer.Symbol}; shares={shares:N0}; cost=${cost:N0}; fee=${fee:N0}; total=${total:N0}; wallet=${backpack.Wallet:N0}." );
		NotifyTerminal( player, NotificationKind.Success, "Stock Purchased", $"Bought {shares:N0} {offer.Symbol} share{Plural( shares )} for ${cost:N0}. Fee ${fee:N0}; total ${total:N0}.", 3f );
	}

	[Rpc.Host]
	public static void RequestSellStock( GameObject player, string symbol, int shares )
	{
		if ( !CallerOwns( player ) )
		{
			Log.Warning( $"[StockTerminal] Sell RPC rejected ownership; player={PlayerLogName( player )}; symbol={symbol}; shares={shares:N0}." );
			return;
		}

		var offer = FinanceCatalog.Stock( symbol );
		if ( offer is null )
		{
			Log.Warning( $"[StockTerminal] Sell RPC rejected unknown stock; player={PlayerLogName( player )}; symbol={symbol}; shares={shares:N0}." );
			return;
		}

		var finance = FinanceFor( player );
		if ( !finance.IsValid() )
		{
			Log.Warning( $"[StockTerminal] Sell RPC rejected missing finance; player={PlayerLogName( player )}; symbol={offer.Symbol}; shares={shares:N0}." );
			return;
		}

		var price = MarketDataSystem.Current?.StockPrice( offer.Symbol ) ?? offer.FallbackPrice;
		if ( shares <= 0 || price <= 0m )
		{
			Log.Warning( $"[StockTerminal] Sell RPC rejected invalid amount; player={PlayerLogName( player )}; symbol={offer.Symbol}; shares={shares:N0}; price=${PriceLog( price )}." );
			NotifyTerminal( player, NotificationKind.Warning, "Stock Order", "Enter a valid share amount.", 2.5f );
			return;
		}

		var owned = finance.StockShares.TryGetValue( offer.Symbol, out var held ) ? held : 0;
		Log.Info( $"[StockTerminal] Sell RPC received; player={PlayerLogName( player )}; symbol={offer.Symbol}; shares={shares:N0}; price=${PriceLog( price )}; owned={owned:N0}." );
		if ( owned < shares )
		{
			Log.Warning( $"[StockTerminal] Sell RPC rejected insufficient shares; player={PlayerLogName( player )}; symbol={offer.Symbol}; shares={shares:N0}; owned={owned:N0}." );
			NotifyTerminal( player, NotificationKind.BadNews, "Not Enough Shares", $"You only hold {owned:N0} {offer.Symbol} share{Plural( owned )}.", 3f );
			return;
		}

		var proceeds = decimal.ToInt32( decimal.Floor( shares * price ) );
		var fee = StockTradeFee( proceeds );
		var netProceeds = int.Max( 0, proceeds - fee );
		Log.Info( $"[StockTerminal] Sell RPC priced; player={PlayerLogName( player )}; symbol={offer.Symbol}; shares={shares:N0}; proceeds=${proceeds:N0}; fee=${fee:N0}; net=${netProceeds:N0}." );
		if ( !finance.TrySellStock( offer.Symbol, shares, price, fee ) )
		{
			Log.Warning( $"[StockTerminal] Sell RPC failed during mutation; player={PlayerLogName( player )}; symbol={offer.Symbol}; shares={shares:N0}; proceeds=${proceeds:N0}; fee=${fee:N0}; net=${netProceeds:N0}." );
			NotifyTerminal( player, NotificationKind.BadNews, "Sell Order Failed", "The order could not be completed.", 3f );
			return;
		}

		AddStockTradeFeeToVault( player, offer.Symbol, proceeds, fee, "sell" );
		Log.Info( $"[StockTerminal] Sell RPC completed; player={PlayerLogName( player )}; symbol={offer.Symbol}; shares={shares:N0}; proceeds=${proceeds:N0}; fee=${fee:N0}; net=${netProceeds:N0}." );
		NotifyTerminal( player, NotificationKind.Success, "Stock Sold", $"Sold {shares:N0} {offer.Symbol} share{Plural( shares )} for ${proceeds:N0}. Fee ${fee:N0}; received ${netProceeds:N0}.", 3f );
	}

	[Rpc.Host]
	public static void RequestBuyCrypto( GameObject player, string coinId, int amount, int sourceAccount )
	{
		if ( !CallerOwns( player ) ) return;
		if ( !System.Enum.IsDefined( typeof( FinanceAccountSource ), sourceAccount ) ) return;

		var price = MarketDataSystem.Current?.CryptoPrice( coinId ) ?? FinanceCatalog.Coin( coinId )?.FallbackPrice ?? 0m;
		FinanceFor( player )?.TryBuyCrypto( coinId, amount, price, (FinanceAccountSource)sourceAccount );
	}

	[Rpc.Host]
	public static void RequestSetRunning( GameObject player, bool running )
	{
		if ( !CallerOwns( player ) ) return;

		UnitFor( player )?.SetRunStaminaDrain( running );
	}

	[Rpc.Host]
	public static void RequestToggleHolster( GameObject player )
	{
		if ( !CallerOwns( player ) ) return;

		BackpackFor( player )?.TryToggleHolster();
	}

	[Rpc.Host]
	public static void RequestReloadWeapon( GameObject player )
	{
		if ( !CallerOwns( player ) ) return;

		BackpackFor( player )?.TryStartReload();
	}

	[Rpc.Host]
	public static void RequestSetWeaponAiming( GameObject player, bool aiming )
	{
		if ( !CallerOwns( player ) ) return;

		BackpackFor( player )?.TrySetWeaponAiming( aiming );
	}

	[Rpc.Host]
	public static void RequestFireWeapon( GameObject player, Vector3 origin, Rotation aim )
	{
		if ( !CallerOwns( player ) ) return;

		BackpackFor( player )?.TryFire( origin, aim );
	}

	[Rpc.Broadcast]
	public static void BroadcastShotDebug( GameObject player, Vector3 position, bool hit )
	{
		if ( !CallerIsHost() ) return;
		if ( !player.IsValid() ) return;

		WeaponBehavior.SpawnDebugMarker( player.Scene, position, hit ? Color.Red : Color.Yellow );
	}

	[Rpc.Broadcast]
	public static void BroadcastNotification( int kind, string title, string message, float shownDuration )
	{
		if ( !CallerIsHost() ) return;

		NotificationSystem.Current?.NotifyFromNetwork( kind, title, message, shownDuration );
	}

	[Rpc.Broadcast]
	public static void BroadcastOpenComputerTerminal( GameObject player )
	{
		if ( !CallerIsHost() ) return;
		if ( !Sandbox.LocalPlayer.Owns( player ) ) return;

		ComputerTerminalSystem.OpenForScene( player.Scene );
	}

	[Rpc.Broadcast]
	public static void BroadcastComputerTerminalMessage( GameObject player, int kind, string title, string message, float shownDuration )
	{
		if ( !CallerIsHost() ) return;
		if ( !Sandbox.LocalPlayer.Owns( player ) ) return;

		ComputerTerminalSystem.ShowMessageForScene( player.Scene, kind, title, message, shownDuration );
	}

	[Rpc.Broadcast]
	public static void BroadcastPlayerNotification( GameObject player, int kind, string title, string message, float shownDuration )
	{
		if ( !CallerIsHost() ) return;
		if ( !Sandbox.LocalPlayer.Owns( player ) ) return;

		NotificationSystem.Current?.NotifyFromNetwork( kind, title, message, shownDuration );
	}

	[Rpc.Host]
	public static void RequestSendChatMessage( GameObject player, string message )
	{
		if ( !CallerOwns( player ) ) return;

		ChatSystem.Current?.TrySendMessage( player, message, Rpc.Caller );
	}

	[Rpc.Broadcast]
	public static void BroadcastChatMessage( GameObject sender, string senderName, string message )
	{
		if ( !CallerIsHost() ) return;

		ChatSystem.Current?.AddNetworkMessage( sender, senderName, message );
	}

	private static Backpack BackpackFor( GameObject player )
	{
		return player?.Components.GetInDescendantsOrSelf<Backpack>();
	}

	private static UnitComponent UnitFor( GameObject player )
	{
		return player?.Components.GetInDescendantsOrSelf<UnitComponent>();
	}

	private static PlayerFinanceComponent FinanceFor( GameObject player )
	{
		return player?.Components.GetInDescendantsOrSelf<PlayerFinanceComponent>();
	}

	private static void NotifyPlayer( GameObject player, NotificationKind kind, string title, string message, float shownDuration )
	{
		BroadcastPlayerNotification( player, (int)kind, title, message, shownDuration );
	}

	private static void NotifyTerminal( GameObject player, NotificationKind kind, string title, string message, float shownDuration )
	{
		BroadcastComputerTerminalMessage( player, (int)kind, title, message, shownDuration );
	}

	private static string AccountDisplayName( FinanceAccountSource source )
	{
		return source == FinanceAccountSource.Bank ? "bank" : "wallet";
	}

	private static string Plural( int count )
	{
		return count == 1 ? "" : "s";
	}

	private static int StockTradeFee( int grossAmount )
	{
		return ServerVaultSystem.Current?.CalculateStockTradeFee( grossAmount )
			?? ServerVaultSystem.CalculateStockTradeFee( grossAmount );
	}

	private static void AddStockTradeFeeToVault( GameObject player, string symbol, int grossAmount, int fee, string side )
	{
		if ( fee <= 0 ) return;

		var vault = ServerVaultSystem.Current;
		if ( vault is null )
		{
			Log.Warning( $"[ServerVault] Stock trade fee was charged but no vault system was available; side={side}; player={PlayerLogName( player )}; symbol={symbol}; gross=${grossAmount:N0}; fee=${fee:N0}." );
			return;
		}

		vault.AddStockTradeFee( player, symbol, grossAmount, fee, side );
	}

	private static string PlayerLogName( GameObject player )
	{
		if ( !player.IsValid() ) return "<invalid>";
		return string.IsNullOrWhiteSpace( player.Name ) ? "<unnamed>" : player.Name;
	}

	private static string PriceLog( decimal price )
	{
		return price.ToString( "N2", System.Globalization.CultureInfo.InvariantCulture );
	}

	private static bool CallerOwns( GameObject gameObject )
	{
		if ( !gameObject.IsValid() ) return false;
		if ( Rpc.Caller is null ) return Networking.IsHost && Sandbox.LocalPlayer.Owns( gameObject );

		var owner = gameObject.Network.Owner;
		if ( owner is not null ) return owner == Rpc.Caller;

		var root = gameObject.Root;
		if ( root.IsValid() && root != gameObject )
		{
			owner = root.Network.Owner;
			if ( owner is not null ) return owner == Rpc.Caller;
		}

		return Rpc.Caller.IsHost && Networking.IsHost && Sandbox.LocalPlayer.Owns( gameObject );
	}

	private static bool CallerIsHost()
	{
		return Rpc.Caller is null ? Networking.IsHost : Rpc.Caller.IsHost;
	}
}
