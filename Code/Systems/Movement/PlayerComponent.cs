using Sandbox;

public sealed class PlayerComponent : Component
{
	private PlayerController _controller;
	private UnitComponent _unit;
	private float _defaultRunSpeed;
	private bool _hasDefaultRunSpeed;
	private bool _sentRunState;
	private bool _lastWantsRun;

	protected override void OnStart()
	{
		FindDependencies();
	}

	protected override void OnUpdate()
	{
		if ( IsProxy ) return;

		FindDependencies();

		var hasMovementInput = HasMovementInput();
		var canSprint = CanSprint( _lastWantsRun );
		ApplySprintSpeedGate( canSprint );

		var wantsRun = Input.Down( "Run" ) && hasMovementInput && canSprint;
		if ( _sentRunState && wantsRun == _lastWantsRun ) return;

		SetRunState( wantsRun );
		_sentRunState = true;
		_lastWantsRun = wantsRun;
	}

	protected override void OnDisabled()
	{
		RestoreRunSpeed();

		if ( _sentRunState && _lastWantsRun )
		{
			SetRunState( false );
		}

		_sentRunState = false;
		_lastWantsRun = false;
	}

	private bool HasMovementInput()
	{
		return Input.Down( "Forward" )
			|| Input.Down( "Backward" )
			|| Input.Down( "Left" )
			|| Input.Down( "Right" );
	}

	private void SetRunState( bool running )
	{
		var player = GameObject.Root;
		if ( !player.IsValid() ) return;

		if ( Networking.IsHost )
		{
			player.Components.GetInDescendantsOrSelf<UnitComponent>()?.SetRunStaminaDrain( running );
			return;
		}

		GameNetworkRpc.RequestSetRunning( player, running );
	}

	private void FindDependencies()
	{
		if ( !_controller.IsValid() )
		{
			_controller = Components.Get<PlayerController>();
			if ( !_controller.IsValid() ) _controller = Components.GetInAncestorsOrSelf<PlayerController>();
		}

		if ( _controller.IsValid() && !_hasDefaultRunSpeed )
		{
			_defaultRunSpeed = _controller.RunSpeed;
			_hasDefaultRunSpeed = true;
		}

		if ( !_unit.IsValid() )
		{
			_unit = GameObject.Root.Components.GetInDescendantsOrSelf<UnitComponent>();
		}
	}

	private bool CanSprint( bool wasRunning )
	{
		if ( !_unit.IsValid() ) return true;

		return wasRunning
			? _unit.HasStaminaToContinueRunning
			: _unit.HasStaminaToStartRunning;
	}

	private void ApplySprintSpeedGate( bool canSprint )
	{
		if ( !_controller.IsValid() || !_hasDefaultRunSpeed ) return;

		var desiredRunSpeed = canSprint ? _defaultRunSpeed : _controller.WalkSpeed;
		if ( _controller.RunSpeed == desiredRunSpeed ) return;

		_controller.RunSpeed = desiredRunSpeed;
	}

	private void RestoreRunSpeed()
	{
		if ( !_controller.IsValid() || !_hasDefaultRunSpeed ) return;

		_controller.RunSpeed = _defaultRunSpeed;
	}
}
