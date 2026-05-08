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
	private const float QuoteRefreshInterval = 300f;
	private const float CandleRefreshInterval = 120f;

	private readonly Dictionary<string, decimal> _stockPrices = new();
	private readonly Dictionary<string, decimal> _cryptoPrices = new();
	private readonly Dictionary<string, List<StockCandle>> _stockCandles = new();
	private readonly Dictionary<string, float> _nextCandleRefresh = new();
	private readonly HashSet<string> _pendingCandleRequests = new();
	private float _nextQuoteRefreshTime;
	private bool _refreshingQuotes;

	public int PriceVersion { get; private set; }
	public int CandleVersion { get; private set; }
	public bool HasLiveCrypto { get; private set; }
	public bool HasLiveStocks { get; private set; }
	public string StatusText { get; private set; } = "Fallback market data";

	public MarketDataSystem( Scene scene ) : base( scene )
	{
		foreach ( var stock in FinanceCatalog.Stocks ) _stockPrices[stock.Symbol] = stock.FallbackPrice;
		foreach ( var coin in FinanceCatalog.Crypto ) _cryptoPrices[coin.Id] = coin.FallbackPrice;

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
		if ( _nextCandleRefresh.TryGetValue( key, out var nextRefresh ) && Time.Now < nextRefresh ) return;

		_nextCandleRefresh[key] = Time.Now + CandleRefreshInterval;
		_pendingCandleRequests.Add( key );
		RefreshStockCandlesAsync( symbol, timeframe, key );
	}

	private void OnTick()
	{
		if ( _refreshingQuotes || Time.Now < _nextQuoteRefreshTime ) return;

		_nextQuoteRefreshTime = Time.Now + QuoteRefreshInterval;
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

			StatusText = HasLiveCrypto || HasLiveStocks ? "Public delayed market data" : "Fallback market data";
			PriceVersion++;
		}
		catch ( Exception e )
		{
			StatusText = "Fallback market data";
			Log.Warning( $"[MarketData] Quote refresh failed: {e.Message}" );
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
			if ( candles.Count <= 0 ) return;

			_stockCandles[key] = candles;
			_stockPrices[symbol] = candles[^1].Close;
			HasLiveStocks = true;
			StatusText = "Public delayed market data";
			PriceVersion++;
			CandleVersion++;
		}
		catch ( Exception e )
		{
			Log.Warning( $"[MarketData] Candle refresh failed for {symbol}: {e.Message}" );
		}
		finally
		{
			_pendingCandleRequests.Remove( key );
		}
	}

	private async System.Threading.Tasks.Task RefreshCryptoAsync()
	{
		var ids = string.Join( ',', FinanceCatalog.Crypto.Select( x => x.Id ) );
		var uri = $"https://api.coingecko.com/api/v3/simple/price?ids={ids}&vs_currencies=usd";
		var json = await HttpGetString( uri );
		using var document = JsonDocument.Parse( json );

		foreach ( var coin in FinanceCatalog.Crypto )
		{
			if ( !document.RootElement.TryGetProperty( coin.Id, out var coinJson ) ) continue;
			if ( !coinJson.TryGetProperty( "usd", out var usd ) ) continue;
			if ( usd.TryGetDecimal( out var price ) && price > 0m ) _cryptoPrices[coin.Id] = price;
		}

		HasLiveCrypto = true;
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

	private static string NormalizeSymbol( string symbol )
	{
		return (symbol ?? "").Trim().ToUpperInvariant();
	}
}
