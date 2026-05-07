using Sandbox;

public sealed class PlayerComponent : Component
{
	private bool _sentRunState;
	private bool _lastWantsRun;

	protected override void OnUpdate()
	{
		if ( IsProxy ) return;

		var wantsRun = Input.Down( "Run" ) && HasMovementInput();
		if ( _sentRunState && wantsRun == _lastWantsRun ) return;

		SetRunState( wantsRun );
		_sentRunState = true;
		_lastWantsRun = wantsRun;
	}

	protected override void OnDisabled()
	{
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
}
