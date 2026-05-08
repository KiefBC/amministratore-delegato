using Sandbox;

public enum NotificationKind
{
	Info,
	Success,
	Warning,
	BadNews,
	Announcement,
	LevelUp,
	Kill,
}

public sealed class GameNotification
{
	private const float MinFadeDuration = 0.1f;

	public int Id { get; set; }
	public NotificationKind Kind { get; set; }
	public string Title { get; set; } = "Notification";
	public string Message { get; set; } = "";
	public float ShownDuration { get; set; } = 3f;
	public float TimeLeft { get; set; } = 6f;
	public float FadeDuration { get; set; } = 3f;

	public string KindClass => Kind.ToString().ToLowerInvariant();
	public bool IsFading => TimeLeft <= FadeDuration;
	public float FadePercent => IsFading ? float.Clamp( TimeLeft / float.Max( FadeDuration, MinFadeDuration ), 0f, 1f ) : 1f;
}

public sealed class NotificationSystem : GameObjectSystem<NotificationSystem>
{
	private const float DefaultShownDuration = 3f;
	private const float DefaultFadeDuration = 3f;
	private const int MaxNotifications = 5;

	private readonly List<GameNotification> _notifications = new();
	private readonly Dictionary<PlayerStatType, int> _lastLevels = new();
	private PlayerStatsComponent _stats;
	private GameObject _displayObject;
	private int _nextId;
	private bool _hasSeededLevels;

	public IReadOnlyList<GameNotification> Notifications => _notifications;

	public NotificationSystem( Scene scene ) : base( scene )
	{
		Listen( Stage.StartUpdate, 50, OnTick, nameof( OnTick ) );
	}

	public GameNotification Notify( NotificationKind kind, string title, string message, float shownDuration = DefaultShownDuration )
	{
		var notification = new GameNotification
		{
			Id = _nextId++,
			Kind = kind,
			Title = string.IsNullOrWhiteSpace( title ) ? DefaultTitleFor( kind ) : title,
			Message = message ?? "",
			ShownDuration = ShownDurationFor( shownDuration ),
			FadeDuration = DefaultFadeDuration,
		};

		notification.TimeLeft = notification.ShownDuration + notification.FadeDuration;
		_notifications.Add( notification );

		while ( _notifications.Count > MaxNotifications )
		{
			_notifications.RemoveAt( 0 );
		}

		return notification;
	}

	public void NotifyLevelUp( PlayerStatType stat, int level )
	{
		Notify( NotificationKind.LevelUp, "Level Up", $"{StatDisplayName( stat )} reached Lv {level}" );
	}

	public void NotifyKill( GameObject target, GameObject attacker )
	{
		if ( !attacker.IsValid() ) return;
		if ( !Sandbox.LocalPlayer.Owns( attacker ) ) return;
		if ( IsSamePlayer( target, attacker ) ) return;

		Notify( NotificationKind.Kill, "Killed", ResolveKillTargetName( target ) );
	}

	public void NotifyFromNetwork( int kind, string title, string message, float shownDuration )
	{
		if ( !System.Enum.IsDefined( typeof( NotificationKind ), kind ) ) return;

		Notify( (NotificationKind)kind, title, message, shownDuration );
	}

	public static string StatDisplayName( PlayerStatType stat )
	{
		return stat switch
		{
			PlayerStatType.Health => "Health",
			PlayerStatType.Stamina => "Stamina",
			PlayerStatType.Punching => "Punching",
			PlayerStatType.Ranged => "Ranged",
			PlayerStatType.Business => "Business",
			_ => "Stat",
		};
	}

	private void OnTick()
	{
		EnsureDisplay();
		PollLevelUps();
		TickNotifications();
	}

	private void EnsureDisplay()
	{
		if ( _displayObject.IsValid() ) return;

		var existing = Scene.GetAllComponents<NotificationDisplay>().FirstOrDefault( x => x.IsValid() );
		if ( existing.IsValid() )
		{
			_displayObject = existing.GameObject;
			return;
		}

		_displayObject = new GameObject( "Notifications" );
		_displayObject.NetworkMode = NetworkMode.Never;

		var screen = _displayObject.Components.Create<ScreenPanel>();
		screen.ZIndex = 110;

		_displayObject.Components.Create<NotificationDisplay>();
	}

	private void PollLevelUps()
	{
		var stats = Sandbox.LocalPlayer.Component<PlayerStatsComponent>( Scene );
		if ( !ReferenceEquals( _stats, stats ) )
		{
			_stats = stats;
			_hasSeededLevels = false;
			_lastLevels.Clear();
		}

		if ( _stats is null ) return;
		if ( !_hasSeededLevels )
		{
			SeedCurrentLevels();
			return;
		}

		foreach ( var stat in PlayerStatsComponent.LeveledStats )
		{
			var level = _stats.GetLevel( stat );
			var previous = _lastLevels.TryGetValue( stat, out var value ) ? value : level;
			if ( level > previous )
			{
				NotifyLevelUp( stat, level );
			}

			_lastLevels[stat] = level;
		}
	}

	private void SeedCurrentLevels()
	{
		if ( _stats is null ) return;

		foreach ( var stat in PlayerStatsComponent.LeveledStats )
		{
			_lastLevels[stat] = _stats.GetLevel( stat );
		}

		_hasSeededLevels = true;
	}

	private void TickNotifications()
	{
		if ( _notifications.Count == 0 ) return;

		for ( var i = _notifications.Count - 1; i >= 0; i-- )
		{
			var notification = _notifications[i];
			notification.TimeLeft -= Time.Delta;
			if ( notification.TimeLeft <= 0f )
			{
				_notifications.RemoveAt( i );
			}
		}
	}

	private static float ShownDurationFor( float requested )
	{
		if ( requested > 0f ) return requested;

		return DefaultShownDuration;
	}

	private static string ResolveKillTargetName( GameObject target )
	{
		if ( !target.IsValid() ) return "Unknown";

		if ( TryGetPlayerDisplayName( target, out var playerName ) ) return playerName;

		var unit = FindUnit( target );
		if ( unit.IsValid() && !string.IsNullOrWhiteSpace( unit.Name ) ) return unit.Name.Trim();

		return string.IsNullOrWhiteSpace( target.Name ) ? "Unknown" : target.Name;
	}

	private static bool TryGetPlayerDisplayName( GameObject target, out string displayName )
	{
		displayName = null;

		var controller = FindPlayerController( target );
		if ( !controller.IsValid() ) return false;

		var owner = controller.GameObject.Network.Owner;
		if ( owner is null ) owner = controller.GameObject.Root.Network.Owner;
		if ( owner is not null && !string.IsNullOrWhiteSpace( owner.DisplayName ) )
		{
			displayName = owner.DisplayName;
			return true;
		}

		var name = controller.GameObject.Root.IsValid() ? controller.GameObject.Root.Name : controller.GameObject.Name;
		const string spawnedPlayerPrefix = "Player - ";
		if ( name.StartsWith( spawnedPlayerPrefix, System.StringComparison.OrdinalIgnoreCase ) )
		{
			name = name[spawnedPlayerPrefix.Length..];
		}

		if ( string.IsNullOrWhiteSpace( name ) ) return false;

		displayName = name.Trim();
		return true;
	}

	private static bool IsSamePlayer( GameObject a, GameObject b )
	{
		var aController = FindPlayerController( a );
		var bController = FindPlayerController( b );

		return aController.IsValid() && bController.IsValid() && aController.GameObject == bController.GameObject;
	}

	private static PlayerController FindPlayerController( GameObject gameObject )
	{
		if ( !gameObject.IsValid() ) return null;

		var controller = gameObject.Components.GetInAncestorsOrSelf<PlayerController>();
		if ( controller.IsValid() ) return controller;

		controller = gameObject.Components.GetInDescendantsOrSelf<PlayerController>();
		if ( controller.IsValid() ) return controller;

		var root = gameObject.Root;
		return root.IsValid() && root != gameObject
			? root.Components.GetInDescendantsOrSelf<PlayerController>()
			: null;
	}

	private static UnitComponent FindUnit( GameObject gameObject )
	{
		if ( !gameObject.IsValid() ) return null;

		var unit = gameObject.Components.GetInAncestorsOrSelf<UnitComponent>();
		if ( unit.IsValid() ) return unit;

		unit = gameObject.Components.GetInDescendantsOrSelf<UnitComponent>();
		if ( unit.IsValid() ) return unit;

		var root = gameObject.Root;
		return root.IsValid() && root != gameObject
			? root.Components.GetInDescendantsOrSelf<UnitComponent>()
			: null;
	}

	private static string DefaultTitleFor( NotificationKind kind )
	{
		return kind switch
		{
			NotificationKind.Success => "Success",
			NotificationKind.Warning => "Warning",
			NotificationKind.BadNews => "Bad News",
			NotificationKind.Announcement => "Announcement",
			NotificationKind.LevelUp => "Level Up",
			NotificationKind.Kill => "Killed",
			_ => "Info",
		};
	}
}
