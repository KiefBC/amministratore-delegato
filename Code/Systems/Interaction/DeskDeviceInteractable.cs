using Sandbox;

/// <summary>
/// Routes desk device use from the player's crosshair to the correct local UI.
/// Each device has its own access rule: computer use requires sitting in the
/// configured chair, while phone use only requires being near the phone target.
/// </summary>
public sealed class DeskDeviceInteractable : Component, IInteractable, IClientInteractable
{
	[Property]
	[Range( 10f, 500f )]
	[Step( 10f )]
	public float Range { get; set; } = 100f;

	[Property]
	[Range( 10f, 500f )]
	[Step( 10f )]
	public float PhoneRange { get; set; } = 100f;

	[Property]
	[Range( 10f, 500f )]
	[Step( 10f )]
	public float ComputerRange { get; set; } = 140f;

	[Property] public BaseChair ComputerChair { get; set; }
	[Property] public GameObject ComputerTarget { get; set; }
	[Property] public GameObject PhoneTarget { get; set; }
	[Property] public bool ComputerRequiresSitting { get; set; } = true;
	[Property] public string ComputerObjectName { get; set; } = "Computer";
	[Property] public string PhoneObjectName { get; set; } = "Cell Phone";
	[Property] public Vector3 ComputerFocusOffset { get; set; } = new( 0f, 0f, 18f );
	[Property] public Vector3 PhoneFocusOffset { get; set; } = new( 0f, 0f, 5f );

	[Property]
	[Range( 0.6f, 0.99f )]
	[Step( 0.01f )]
	public float MinFocusDot { get; set; } = 0.6f;

	[Property]
	[Range( 50f, 500f )]
	[Step( 10f )]
	public float FocusMaxDistance { get; set; } = 300f;

	Vector3 IInteractable.InteractPosition => InteractPositionFor( ResolveFocusedDevice( Sandbox.LocalPlayer.GameObject( Scene ) ) );
	float IInteractable.InteractRange => float.Max( Range, float.Max( PhoneRange, ComputerRange ) );
	string IInteractable.Prompt => PromptFor( ResolveFocusedDevice( Sandbox.LocalPlayer.GameObject( Scene ) ) );
	bool IInteractable.CanInteract( GameObject player ) => ResolveFocusedDevice( player ) != DeskDeviceType.None;

	void IInteractable.Interact( GameObject player )
	{
		if ( !Networking.IsHost ) return;

		var device = ResolveFocusedDevice( player );
		if ( device == DeskDeviceType.None )
		{
			Log.Info( $"[DeskDevice] {PlayerLogName( player )} pressed Use at desk but no device was focused. {FocusDebug( player )}" );
			return;
		}

		TryUseDeviceOnHost( player, device );
	}

	bool IClientInteractable.TryClientInteract( GameObject player )
	{
		var device = ResolveFocusedDevice( player );
		if ( device == DeskDeviceType.None )
		{
			Log.Info( $"[DeskDevice] {PlayerLogName( player )} pressed Use at desk but no device was focused. {FocusDebug( player )}" );
			return true;
		}

		GameNetworkRpc.RequestUseDeskDevice( GameObject, player, (int)device );
		return true;
	}

	public void TryUseDeviceOnHost( GameObject player, DeskDeviceType device )
	{
		if ( !Networking.IsHost ) return;
		if ( !player.IsValid() || device == DeskDeviceType.None ) return;

		if ( !ResolveDeviceTarget( device ).IsValid() )
		{
			Log.Warning( $"[DeskDevice] {PlayerLogName( player )} {device} rejected; target is missing." );
			return;
		}

		if ( !CanUseDevice( player, device ) )
		{
			Log.Info( $"[DeskDevice] {PlayerLogName( player )} {device} rejected; {DeviceRejectReason( player, device )}." );
			return;
		}

		Log.Info( $"[DeskDevice] {PlayerLogName( player )} interacted with {device}." );

		if ( Sandbox.LocalPlayer.Owns( player ) )
		{
			OpenLocalDevice( player.Scene, device );
			return;
		}

		switch ( device )
		{
			case DeskDeviceType.Computer:
				GameNetworkRpc.BroadcastOpenComputerTerminal( player );
				break;
			case DeskDeviceType.Phone:
				GameNetworkRpc.BroadcastOpenPhoneBook( player );
				break;
		}
	}

	private DeskDeviceType ResolveFocusedDevice( GameObject player )
	{
		if ( !TryGetLookRay( player, out var origin, out var forward ) ) return DeskDeviceType.None;
		if ( TryResolveTraceFocusedDevice( player, origin, forward, out var tracedDevice ) )
		{
			return CanUseDevice( player, tracedDevice ) ? tracedDevice : DeskDeviceType.None;
		}

		return DeskDeviceType.None;
	}

	private bool TryResolveTraceFocusedDevice( GameObject player, Vector3 origin, Vector3 forward, out DeskDeviceType device )
	{
		device = DeskDeviceType.None;

		var end = origin + forward * FocusMaxDistance;
		var trace = Scene.Trace.Ray( origin, end )
			.IgnoreGameObjectHierarchy( player )
			.Run();

		if ( !trace.Hit || !trace.GameObject.IsValid() ) return false;

		var computer = ResolveComputerTarget();
		var phone = ResolvePhoneTarget();

		if ( IsAtOrBelow( trace.GameObject, phone ) )
		{
			device = DeskDeviceType.Phone;
			return true;
		}

		if ( IsAtOrBelow( trace.GameObject, computer ) )
		{
			device = DeskDeviceType.Computer;
			return true;
		}

		return false;
	}

	private bool CanUseDevice( GameObject player, DeskDeviceType device )
	{
		if ( !player.IsValid() ) return false;
		if ( !ResolveDeviceTarget( device ).IsValid() ) return false;

		return device switch
		{
			DeskDeviceType.Computer => CanUseComputer( player ),
			DeskDeviceType.Phone => IsNearPhone( player ),
			_ => false
		};
	}

	private bool CanUseComputer( GameObject player )
	{
		if ( !ComputerRequiresSitting ) return IsNearComputer( player );
		return IsPlayerSittingAtComputer( player );
	}

	private bool IsPlayerSittingAtComputer( GameObject player )
	{
		var chair = ResolveComputerChair();
		var controller = PlayerControllerFor( player );
		if ( !chair.IsValid() || !controller.IsValid() ) return false;
		if ( !chair.IsOccupied ) return false;

		var occupant = chair.GetOccupant();
		return occupant.IsValid() && occupant == controller;
	}

	private bool IsNearComputer( GameObject player )
	{
		var target = ResolveComputerTarget();
		return target.IsValid() && player.WorldPosition.DistanceSquared( target.WorldPosition ) <= ComputerRange * ComputerRange;
	}

	private bool IsNearPhone( GameObject player )
	{
		var target = ResolvePhoneTarget();
		return target.IsValid() && player.WorldPosition.DistanceSquared( target.WorldPosition ) <= PhoneRange * PhoneRange;
	}

	private string DeviceRejectReason( GameObject player, DeskDeviceType device )
	{
		return device switch
		{
			DeskDeviceType.Computer when ComputerRequiresSitting && !IsPlayerSittingAtComputer( player ) => "player is not seated at the computer chair",
			DeskDeviceType.Computer when !IsNearComputer( player ) => $"player is outside computer range ({ComputerRange:0.#})",
			DeskDeviceType.Phone when !IsNearPhone( player ) => $"player is outside phone range ({PhoneRange:0.#})",
			_ => "device access rule failed"
		};
	}

	private bool TryGetLookRay( GameObject player, out Vector3 origin, out Vector3 forward )
	{
		var camera = Scene?.Camera;
		if ( camera.IsValid() )
		{
			origin = camera.WorldPosition;
			forward = camera.WorldRotation.Forward;
			return true;
		}

		var controller = player?.Components.GetInDescendantsOrSelf<PlayerController>();
		if ( controller.IsValid() )
		{
			origin = controller.EyePosition;
			forward = player.WorldRotation.Forward;
			return true;
		}

		origin = default;
		forward = default;
		return false;
	}

	private float FocusScore( Vector3 origin, Vector3 forward, Vector3 target )
	{
		if ( target == default ) return -1f;

		var toTarget = target - origin;
		if ( toTarget.Length <= 1f || toTarget.Length > FocusMaxDistance ) return -1f;

		var direction = toTarget.Normal;
		return forward.x * direction.x + forward.y * direction.y + forward.z * direction.z;
	}

	private string FocusDebug( GameObject player )
	{
		if ( !TryGetLookRay( player, out var origin, out var forward ) ) return "lookRay=invalid";

		var traceHit = TraceDebug( player, origin, forward );
		return $"trace=({traceHit}); minDot={MinFocusDot:0.##}; maxDistance={FocusMaxDistance:0.#}; computerAccess={CanUseDevice( player, DeskDeviceType.Computer )}; phoneAccess={CanUseDevice( player, DeskDeviceType.Phone )}; computer=({FocusDebugFor( origin, forward, ResolveComputerTarget(), ComputerFocusOffset )}); phone=({FocusDebugFor( origin, forward, ResolvePhoneTarget(), PhoneFocusOffset )})";
	}

	private string TraceDebug( GameObject player, Vector3 origin, Vector3 forward )
	{
		var trace = Scene.Trace.Ray( origin, origin + forward * FocusMaxDistance )
			.IgnoreGameObjectHierarchy( player )
			.Run();

		return trace.Hit && trace.GameObject.IsValid()
			? $"hit={trace.GameObject.Name}; distance={(trace.HitPosition - origin).Length:0.#}"
			: "hit=none";
	}

	private string FocusDebugFor( Vector3 origin, Vector3 forward, GameObject target, Vector3 offset )
	{
		if ( !target.IsValid() ) return "target=invalid";

		var focus = FocusPosition( target, offset );
		var toTarget = focus - origin;
		var distance = toTarget.Length;
		var dot = FocusScore( origin, forward, focus );
		return $"target={target.Name}; distance={distance:0.#}; dot={dot:0.###}";
	}

	private Vector3 FocusPosition( GameObject target, Vector3 offset )
	{
		return target.IsValid() ? target.WorldPosition + offset : default;
	}

	private Vector3 InteractPositionFor( DeskDeviceType device )
	{
		return device switch
		{
			DeskDeviceType.Computer => ResolveComputerTarget().IsValid() ? ResolveComputerTarget().WorldPosition : WorldPosition,
			DeskDeviceType.Phone => ResolvePhoneTarget().IsValid() ? ResolvePhoneTarget().WorldPosition : WorldPosition,
			_ => WorldPosition
		};
	}

	private GameObject ResolveDeviceTarget( DeskDeviceType device )
	{
		return device switch
		{
			DeskDeviceType.Computer => ResolveComputerTarget(),
			DeskDeviceType.Phone => ResolvePhoneTarget(),
			_ => null
		};
	}

	private GameObject ResolveComputerTarget()
	{
		if ( ComputerTarget.IsValid() ) return ComputerTarget;
		ComputerTarget = FindDeskChild( ComputerObjectName );
		return ComputerTarget;
	}

	private GameObject ResolvePhoneTarget()
	{
		if ( PhoneTarget.IsValid() ) return PhoneTarget;
		PhoneTarget = FindDeskChild( PhoneObjectName );
		return PhoneTarget;
	}

	private BaseChair ResolveComputerChair()
	{
		if ( ComputerChair.IsValid() ) return ComputerChair;

		ComputerChair = Components.Get<BaseChair>();
		if ( ComputerChair.IsValid() ) return ComputerChair;

		ComputerChair = Components.GetInAncestorsOrSelf<BaseChair>();
		return ComputerChair;
	}

	private GameObject FindDeskChild( string objectName )
	{
		if ( string.IsNullOrWhiteSpace( objectName ) ) return null;

		var parent = GameObject.Parent;
		foreach ( var go in Scene.GetAllObjects( true ) )
		{
			if ( !go.IsValid() || !MatchesObjectName( go.Name, objectName ) ) continue;
			if ( parent.IsValid() && go.Parent != parent ) continue;

			return go;
		}

		return null;
	}

	private static bool MatchesObjectName( string candidate, string configuredName )
	{
		if ( string.IsNullOrWhiteSpace( candidate ) || string.IsNullOrWhiteSpace( configuredName ) ) return false;
		if ( candidate.Equals( configuredName, System.StringComparison.OrdinalIgnoreCase ) ) return true;
		return candidate.StartsWith( configuredName + " ", System.StringComparison.OrdinalIgnoreCase );
	}

	private static bool IsAtOrBelow( GameObject candidate, GameObject ancestor )
	{
		if ( !candidate.IsValid() || !ancestor.IsValid() ) return false;

		var current = candidate;
		while ( current.IsValid() )
		{
			if ( current == ancestor ) return true;
			current = current.Parent;
		}

		return false;
	}

	private static PlayerController PlayerControllerFor( GameObject player )
	{
		if ( !player.IsValid() ) return null;

		var controller = player.Components.GetInAncestorsOrSelf<PlayerController>();
		if ( !controller.IsValid() ) controller = player.Components.GetInDescendantsOrSelf<PlayerController>();

		return controller;
	}

	private static void OpenLocalDevice( Scene scene, DeskDeviceType device )
	{
		switch ( device )
		{
			case DeskDeviceType.Computer:
				ComputerTerminalSystem.OpenForScene( scene );
				break;
			case DeskDeviceType.Phone:
				PhoneBookSystem.OpenForScene( scene );
				break;
		}
	}

	private static string PromptFor( DeskDeviceType device )
	{
		return device switch
		{
			DeskDeviceType.Computer => "Press E to Use Stock Terminal",
			DeskDeviceType.Phone => "Press E to Use Phone Book",
			_ => ""
		};
	}

	private static string PlayerLogName( GameObject player )
	{
		if ( player.IsValid() && !string.IsNullOrWhiteSpace( player.Name ) ) return player.Name;
		return "unknown player";
	}
}
