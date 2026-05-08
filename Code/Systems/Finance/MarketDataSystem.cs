using Sandbox;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading;

public enum StockTimeframe
{
	OneHour,
	OneDay,
	SevenDays,
}

public readonly record struct StockCandle( long Timestamp, decimal Open, decimal High, decimal Low, decimal Close, long Volume )
{
	public bool IsUp => Close >= Open;
}

public sealed class MarketDataSystem : GameObjectSystem<MarketDataSystem>
{
	private const string CacheFileName = "market_data_cache.json";
	private const float QuoteRefreshPollInterval = 60f;
	private const long StockCandleRefreshIntervalSeconds = 300;
	private const long CryptoRefreshIntervalSeconds = 900;
	private const long SevenDayCandleRefreshIntervalSeconds = 3600;
	private const long RateLimitBackoffSeconds = 3600;

	private readonly Dictionary<string, decimal> _stockPrices = new();
	private readonly Dictionary<string, long> _stockPriceTimestamps = new();
	private readonly Dictionary<string, decimal> _cryptoPrices = new();
	private readonly Dictionary<string, List<StockCandle>> _stockCandles = new();
	private readonly Dictionary<string, float> _nextCandleRefresh = new();
	private readonly Dictionary<string, long> _loggedCandleCacheSkips = new();
	private readonly HashSet<string> _pendingCandleRequests = new();
	private float _nextQuoteRefreshTime;
	private bool _refreshingQuotes;
	private long _loggedCryptoCacheSkipUntil;
	private MarketDataCache _cache = new();

	public int PriceVersion { get; private set; }
	public int CandleVersion { get; private set; }
	public bool HasLiveCrypto { get; private set; }
	public bool HasLiveStocks { get; private set; }
	public string StatusText { get; private set; } = "Fallback market data";

	public MarketDataSystem( Scene scene ) : base( scene )
	{
		foreach ( var stock in FinanceCatalog.Stocks ) _stockPrices[stock.Symbol] = stock.FallbackPrice;
		foreach ( var coin in FinanceCatalog.Crypto ) _cryptoPrices[coin.Id] = coin.FallbackPrice;
		LoadMarketCache();

		PriceVersion++;
		CandleVersion++;
		Listen( Stage.StartUpdate, 0, OnTick, nameof( OnTick ) );
	}

	public decimal StockPrice( string symbol )
	{
		symbol = NormalizeSymbol( symbol );
		if ( _stockPrices.TryGetValue( symbol, out var price ) ) return price;
		return FinanceCatalog.Stock( symbol )?.FallbackPrice ?? 1m;
	}

	public decimal CryptoPrice( string id )
	{
		if ( _cryptoPrices.TryGetValue( id ?? "", out var price ) ) return price;
		return FinanceCatalog.Coin( id )?.FallbackPrice ?? 1m;
	}

	public IReadOnlyList<StockCandle> StockCandles( string symbol, StockTimeframe timeframe )
	{
		symbol = NormalizeSymbol( symbol );
		RequestStockCandles( symbol, timeframe );

		var key = CandleKey( symbol, timeframe );
		if ( _stockCandles.TryGetValue( key, out var candles ) && candles.Count > 0 ) return candles;

		candles = BuildFallbackCandles( symbol, timeframe ).ToList();
		_stockCandles[key] = candles;
		return candles;
	}

	public void RequestStockCandles( string symbol, StockTimeframe timeframe )
	{
		symbol = NormalizeSymbol( symbol );
		if ( string.IsNullOrWhiteSpace( symbol ) ) return;

		var key = CandleKey( symbol, timeframe );
		if ( _pendingCandleRequests.Contains( key ) ) return;
		if ( ShouldUseCachedCandles( key ) ) return;
		if ( _nextCandleRefresh.TryGetValue( key, out var nextRefresh ) && Time.Now < nextRefresh ) return;

		_nextCandleRefresh[key] = Time.Now + 1f;
		_pendingCandleRequests.Add( key );
		RefreshStockCandlesAsync( symbol, timeframe, key );
	}

	private void OnTick()
	{
		if ( _refreshingQuotes || Time.Now < _nextQuoteRefreshTime ) return;

		_nextQuoteRefreshTime = Time.Now + QuoteRefreshPollInterval;
		RefreshQuotesAsync();
	}

	private async void RefreshQuotesAsync()
	{
		_refreshingQuotes = true;

		try
		{
			await RefreshCryptoAsync();

			foreach ( var stock in FinanceCatalog.Stocks )
			{
				RequestStockCandles( stock.Symbol, StockTimeframe.OneDay );
			}

			StatusText = HasLiveCrypto || HasLiveStocks ? "Cached/public delayed market data" : "Fallback market data";
			PriceVersion++;
		}
		catch ( Exception e )
		{
			StatusText = HasLiveCrypto || HasLiveStocks ? "Cached market data" : "Fallback market data";
			Log.Warning( $"[MarketData] Quote refresh failed unexpectedly: {e.Message}" );
		}
		finally
		{
			_refreshingQuotes = false;
		}
	}

	private async void RefreshStockCandlesAsync( string symbol, StockTimeframe timeframe, string key )
	{
		try
		{
			var response = await HttpGetString( YahooChartUrl( symbol, timeframe ) );
			var candles = ParseYahooCandles( response, timeframe );
			if ( candles.Count <= 0 )
			{
				SetCandleNextRefresh( key, UnixNow() + StockRefreshSeconds( timeframe ) );
				return;
			}

			_stockCandles[key] = candles;
			UpdateStockPrice( symbol, candles );
			CacheStockCandles( key, candles, timeframe );
			_cache.NextCandleRefreshUnix.TryGetValue( key, out var nextRefresh );
			HasLiveStocks = true;
			StatusText = "Public delayed market data";
			Log.Info( $"[MarketData] Candle refresh succeeded for {symbol} {TimeframeLogLabel( timeframe )}; candles={candles.Count:N0}; close=${candles[^1].Close:N2}; nextRefresh={FormatUnixTime( nextRefresh )}." );
			PriceVersion++;
			CandleVersion++;
		}
		catch ( Exception e )
		{
			var backoff = IsRateLimited( e ) ? RateLimitBackoffSeconds : StockRefreshSeconds( timeframe );
			SetCandleNextRefresh( key, UnixNow() + backoff );
			StatusText = _stockCandles.ContainsKey( key ) ? "Cached market data" : StatusText;

			if ( IsRateLimited( e ) )
			{
				Log.Warning( $"[MarketData] Candle refresh rate-limited for {symbol}; using cached/fallback data for {backoff / 60:N0} minutes." );
				GameLogSystem.Current?.Warning( "system", "Stock candle refresh rate-limited", data: GameLogSystem.Fields(
					("symbol", symbol),
					("timeframe", TimeframeLogLabel( timeframe )),
					("backoffSeconds", backoff) ) );
			}
			else
			{
				Log.Warning( $"[MarketData] Candle refresh failed for {symbol}: {e.Message}" );
			}
		}
		finally
		{
			_pendingCandleRequests.Remove( key );
		}
	}

	private async System.Threading.Tasks.Task RefreshCryptoAsync()
	{
		var now = UnixNow();
		if ( _cache.NextCryptoRefreshUnix > now )
		{
			LogCryptoCacheSkip( _cache.NextCryptoRefreshUnix );
			return;
		}

		var ids = string.Join( ',', FinanceCatalog.Crypto.Select( x => x.Id ) );
		var uri = $"https://api.coingecko.com/api/v3/simple/price?ids={ids}&vs_currencies=usd";
		try
		{
			var json = await HttpGetString( uri );
			using var document = JsonDocument.Parse( json );
			var updated = 0;

			foreach ( var coin in FinanceCatalog.Crypto )
			{
				if ( !document.RootElement.TryGetProperty( coin.Id, out var coinJson ) ) continue;
				if ( !coinJson.TryGetProperty( "usd", out var usd ) ) continue;
				if ( !usd.TryGetDecimal( out var price ) || price <= 0m ) continue;

				_cryptoPrices[coin.Id] = price;
				_cache.CryptoPrices[coin.Id] = new CachedPrice
				{
					FetchedAtUnix = now,
					Price = price,
				};
				updated++;
			}

			_cache.NextCryptoRefreshUnix = now + CryptoRefreshIntervalSeconds;
			HasLiveCrypto = true;
			SaveMarketCache();
			Log.Info( $"[MarketData] Crypto refresh succeeded; prices={updated:N0}; nextRefresh={FormatUnixTime( _cache.NextCryptoRefreshUnix )}." );
		}
		catch ( Exception e )
		{
			var backoff = IsRateLimited( e ) ? RateLimitBackoffSeconds : CryptoRefreshIntervalSeconds;
			_cache.NextCryptoRefreshUnix = now + backoff;
			SaveMarketCache();

			if ( IsRateLimited( e ) )
			{
				Log.Warning( $"[MarketData] Crypto refresh rate-limited; using cached/fallback data for {backoff / 60:N0} minutes." );
				GameLogSystem.Current?.Warning( "system", "Crypto refresh rate-limited", data: GameLogSystem.Fields(
					("backoffSeconds", backoff) ) );
			}
			else
			{
				Log.Warning( $"[MarketData] Crypto refresh failed: {e.Message}" );
			}
		}
	}

	private static string YahooChartUrl( string symbol, StockTimeframe timeframe )
	{
		var (range, interval) = timeframe switch
		{
			StockTimeframe.OneHour => ("1d", "1m"),
			StockTimeframe.OneDay => ("1d", "5m"),
			StockTimeframe.SevenDays => ("7d", "1h"),
			_ => ("1d", "5m"),
		};

		return $"https://query1.finance.yahoo.com/v8/finance/chart/{symbol}?range={range}&interval={interval}";
	}

	private static List<StockCandle> ParseYahooCandles( string json, StockTimeframe timeframe )
	{
		var candles = new List<StockCandle>();
		using var document = JsonDocument.Parse( json );

		var root = document.RootElement;
		if ( !root.TryGetProperty( "chart", out var chart ) ) return candles;
		if ( !chart.TryGetProperty( "result", out var results ) || results.ValueKind != JsonValueKind.Array || results.GetArrayLength() <= 0 ) return candles;

		var result = results[0];
		if ( !result.TryGetProperty( "timestamp", out var timestamps ) ) return candles;
		if ( !result.TryGetProperty( "indicators", out var indicators ) ) return candles;
		if ( !indicators.TryGetProperty( "quote", out var quotes ) || quotes.ValueKind != JsonValueKind.Array || quotes.GetArrayLength() <= 0 ) return candles;

		var quote = quotes[0];
		if ( !quote.TryGetProperty( "open", out var opens ) ) return candles;
		if ( !quote.TryGetProperty( "high", out var highs ) ) return candles;
		if ( !quote.TryGetProperty( "low", out var lows ) ) return candles;
		if ( !quote.TryGetProperty( "close", out var closes ) ) return candles;
		quote.TryGetProperty( "volume", out var volumes );

		var count = int.Min( timestamps.GetArrayLength(), int.Min( opens.GetArrayLength(), int.Min( highs.GetArrayLength(), int.Min( lows.GetArrayLength(), closes.GetArrayLength() ) ) ) );
		for ( var i = 0; i < count; i++ )
		{
			if ( !TryGetDecimal( opens[i], out var open ) ) continue;
			if ( !TryGetDecimal( highs[i], out var high ) ) continue;
			if ( !TryGetDecimal( lows[i], out var low ) ) continue;
			if ( !TryGetDecimal( closes[i], out var close ) ) continue;

			var timestamp = timestamps[i].TryGetInt64( out var ts ) ? ts : 0;
			var volume = volumes.ValueKind == JsonValueKind.Array && i < volumes.GetArrayLength() && volumes[i].TryGetInt64( out var vol ) ? vol : 0;
			candles.Add( new StockCandle( timestamp, open, high, low, close, volume ) );
		}

		if ( timeframe == StockTimeframe.OneHour && candles.Count > 60 )
		{
			candles = candles.Skip( candles.Count - 60 ).ToList();
		}

		return candles;
	}

	private IEnumerable<StockCandle> BuildFallbackCandles( string symbol, StockTimeframe timeframe )
	{
		var offer = FinanceCatalog.Stock( symbol );
		var basePrice = offer?.FallbackPrice ?? 100m;
		var count = timeframe switch
		{
			StockTimeframe.OneHour => 24,
			StockTimeframe.OneDay => 36,
			StockTimeframe.SevenDays => 42,
			_ => 36,
		};

		var seed = 0;
		foreach ( var ch in symbol ?? "" ) seed += ch;

		for ( var i = 0; i < count; i++ )
		{
			var wave = (decimal)MathF.Sin( (i + seed) * 0.52f ) * 1.8m;
			var drift = (i - count / 2m) * ((offer?.ChangePercent ?? 0m) / 100m);
			var open = decimal.Max( 1m, basePrice + wave + drift );
			var close = decimal.Max( 1m, open + (decimal)MathF.Sin( (i + seed) * 0.87f ) * 1.15m );
			var high = decimal.Max( open, close ) + 0.85m;
			var low = decimal.Max( 1m, decimal.Min( open, close ) - 0.85m );
			yield return new StockCandle( i, open, high, low, close, 0 );
		}
	}

	private void LoadMarketCache()
	{
		try
		{
			_cache = FileSystem.Data.ReadJsonOrDefault( CacheFileName, new MarketDataCache() ) ?? new MarketDataCache();
		}
		catch ( Exception e )
		{
			_cache = new MarketDataCache();
			Log.Warning( $"[MarketData] Cache load failed: {e.Message}" );
		}

		_cache.StockCandles ??= new Dictionary<string, CachedCandleSet>();
		_cache.NextCandleRefreshUnix ??= new Dictionary<string, long>();
		_cache.CryptoPrices ??= new Dictionary<string, CachedPrice>();

		var loadedStockCandleSets = 0;
		foreach ( var pair in _cache.StockCandles.ToArray() )
		{
			var cached = pair.Value;
			if ( cached?.Candles is null || cached.Candles.Count <= 0 ) continue;

			var candles = cached.Candles.Select( x => new StockCandle( x.Timestamp, x.Open, x.High, x.Low, x.Close, x.Volume ) ).ToList();
			if ( candles.Count <= 0 ) continue;

			_stockCandles[pair.Key] = candles;
			var symbol = SymbolFromCandleKey( pair.Key );
			if ( !string.IsNullOrWhiteSpace( symbol ) ) UpdateStockPrice( symbol, candles );
			HasLiveStocks = true;
			loadedStockCandleSets++;
		}

		var loadedCryptoPrices = 0;
		foreach ( var pair in _cache.CryptoPrices.ToArray() )
		{
			if ( pair.Value is null || pair.Value.Price <= 0m ) continue;
			_cryptoPrices[pair.Key] = pair.Value.Price;
			HasLiveCrypto = true;
			loadedCryptoPrices++;
		}

		if ( HasLiveCrypto || HasLiveStocks ) StatusText = "Cached market data";
		Log.Info( $"[MarketData] Cache loaded from {CacheFileName}; stockCandleSets={loadedStockCandleSets:N0}; cryptoPrices={loadedCryptoPrices:N0}; nextCryptoRefresh={FormatUnixTime( _cache.NextCryptoRefreshUnix )}." );
		GameLogSystem.Current?.Info( "system", "Market data cache loaded", data: GameLogSystem.Fields(
			("file", CacheFileName),
			("stockCandleSets", loadedStockCandleSets),
			("cryptoPrices", loadedCryptoPrices),
			("nextCryptoRefresh", FormatUnixTime( _cache.NextCryptoRefreshUnix )) ) );
	}

	private void SaveMarketCache()
	{
		try
		{
			_cache.Version = 1;
			FileSystem.Data.WriteJson( CacheFileName, _cache );
			Log.Info( $"[MarketData] Cache saved to {CacheFileName}; stockCandleSets={_cache.StockCandles.Count:N0}; cryptoPrices={_cache.CryptoPrices.Count:N0}." );
		}
		catch ( Exception e )
		{
			Log.Warning( $"[MarketData] Cache save failed: {e.Message}" );
		}
	}

	private bool ShouldUseCachedCandles( string key )
	{
		var now = UnixNow();
		if ( _cache.NextCandleRefreshUnix.TryGetValue( key, out var nextRefresh ) && now < nextRefresh )
		{
			LogCandleCacheSkip( key, nextRefresh );
			return true;
		}

		return false;
	}

	private void LogCandleCacheSkip( string key, long nextRefresh )
	{
		if ( _loggedCandleCacheSkips.TryGetValue( key, out var loggedRefresh ) && loggedRefresh == nextRefresh ) return;

		_loggedCandleCacheSkips[key] = nextRefresh;
		Log.Info( $"[MarketData] Candle cache hit for {key}; skipping HTTP until {FormatUnixTime( nextRefresh )}." );
	}

	private void LogCryptoCacheSkip( long nextRefresh )
	{
		if ( _loggedCryptoCacheSkipUntil == nextRefresh ) return;

		_loggedCryptoCacheSkipUntil = nextRefresh;
		Log.Info( $"[MarketData] Crypto cache hit; skipping HTTP until {FormatUnixTime( nextRefresh )}." );
	}

	private void CacheStockCandles( string key, List<StockCandle> candles, StockTimeframe timeframe )
	{
		var now = UnixNow();
		_cache.StockCandles[key] = new CachedCandleSet
		{
			FetchedAtUnix = now,
			Candles = candles.Select( x => new CachedStockCandle
			{
				Timestamp = x.Timestamp,
				Open = x.Open,
				High = x.High,
				Low = x.Low,
				Close = x.Close,
				Volume = x.Volume,
			} ).ToList(),
		};

		SetCandleNextRefresh( key, now + StockRefreshSeconds( timeframe ), save: false );
		SaveMarketCache();
	}

	private void UpdateStockPrice( string symbol, List<StockCandle> candles )
	{
		symbol = NormalizeSymbol( symbol );
		if ( string.IsNullOrWhiteSpace( symbol ) || candles.Count <= 0 ) return;

		var latest = candles[^1];
		if ( _stockPriceTimestamps.TryGetValue( symbol, out var currentTimestamp ) && latest.Timestamp < currentTimestamp ) return;

		_stockPrices[symbol] = latest.Close;
		_stockPriceTimestamps[symbol] = latest.Timestamp;
	}

	private void SetCandleNextRefresh( string key, long nextRefreshUnix, bool save = true )
	{
		_cache.NextCandleRefreshUnix[key] = nextRefreshUnix;
		if ( save ) SaveMarketCache();
	}

	private static long StockRefreshSeconds( StockTimeframe timeframe )
	{
		return timeframe switch
		{
			StockTimeframe.SevenDays => SevenDayCandleRefreshIntervalSeconds,
			_ => StockCandleRefreshIntervalSeconds,
		};
	}

	private static bool IsRateLimited( Exception e )
	{
		var message = e.Message ?? "";
		return message.Contains( "429", StringComparison.OrdinalIgnoreCase )
			|| message.Contains( "Too Many Requests", StringComparison.OrdinalIgnoreCase );
	}

	private static string TimeframeLogLabel( StockTimeframe timeframe )
	{
		return timeframe switch
		{
			StockTimeframe.OneHour => "1H",
			StockTimeframe.OneDay => "1D",
			StockTimeframe.SevenDays => "7D",
			_ => timeframe.ToString(),
		};
	}

	private static string FormatUnixTime( long unix )
	{
		return unix <= 0
			? "not scheduled"
			: DateTimeOffset.FromUnixTimeSeconds( unix ).UtcDateTime.ToString( "u", CultureInfo.InvariantCulture );
	}

	private static long UnixNow()
	{
		return DateTimeOffset.UtcNow.ToUnixTimeSeconds();
	}

	private static bool TryGetDecimal( JsonElement element, out decimal value )
	{
		value = 0m;
		return element.ValueKind != JsonValueKind.Null && element.ValueKind != JsonValueKind.Undefined && element.TryGetDecimal( out value );
	}

	private static async System.Threading.Tasks.Task<string> HttpGetString( string uri )
	{
		return await Sandbox.Http.RequestStringAsync( uri, "GET", null, null, CancellationToken.None );
	}

	private static string CandleKey( string symbol, StockTimeframe timeframe )
	{
		return $"{NormalizeSymbol( symbol )}:{timeframe}";
	}

	private static string SymbolFromCandleKey( string key )
	{
		if ( string.IsNullOrWhiteSpace( key ) ) return "";

		var separator = key.IndexOf( ':' );
		return separator > 0 ? NormalizeSymbol( key[..separator] ) : NormalizeSymbol( key );
	}

	private static string NormalizeSymbol( string symbol )
	{
		return (symbol ?? "").Trim().ToUpperInvariant();
	}

	private sealed class MarketDataCache
	{
		public int Version { get; set; } = 1;
		public Dictionary<string, CachedCandleSet> StockCandles { get; set; } = new();
		public Dictionary<string, long> NextCandleRefreshUnix { get; set; } = new();
		public Dictionary<string, CachedPrice> CryptoPrices { get; set; } = new();
		public long NextCryptoRefreshUnix { get; set; }
	}

	private sealed class CachedCandleSet
	{
		public long FetchedAtUnix { get; set; }
		public List<CachedStockCandle> Candles { get; set; } = new();
	}

	private sealed class CachedStockCandle
	{
		public long Timestamp { get; set; }
		public decimal Open { get; set; }
		public decimal High { get; set; }
		public decimal Low { get; set; }
		public decimal Close { get; set; }
		public long Volume { get; set; }
	}

	private sealed class CachedPrice
	{
		public long FetchedAtUnix { get; set; }
		public decimal Price { get; set; }
	}
}
