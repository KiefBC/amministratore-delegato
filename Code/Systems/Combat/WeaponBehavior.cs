using Sandbox;
using Sandbox.Citizen;

/// <summary>
/// Firearm behavior — aim, fire, holster, reload. Reads the currently equipped weapon
/// from <see cref="Equipment"/> on the same GameObject; stats (Damage/Range/HoldType/
/// MagazineSize/ReloadDuration/offsets) are copied in by <see cref="Equipment.Equip"/>
/// from the world <see cref="WeaponPickup"/>.
///
/// Firing behavior is gated by a small state machine — see <see cref="WeaponState"/>.
/// </summary>
public sealed class WeaponBehavior : Component
{
	[Property] public SkinnedModelRenderer BodyRenderer { get; set; }
	[Property] public CitizenAnimationHelper AnimHelper { get; set; }
	[Property] public Equipment Equipment { get; set; }

	private UIStateManager _uiState;

	[Property]
	public CitizenAnimationHelper.HoldTypes HoldType { get; set; }
		= CitizenAnimationHelper.HoldTypes.Pistol;

	[Property]
	public CitizenAnimationHelper.Hand Handedness { get; set; }
		= CitizenAnimationHelper.Hand.Right;

	/// <summary>
	/// Damage dealt per shot.
	/// </summary>
	[Property]
	[Range( 1f, 200f )]
	[Step( 1f )]
	public float Damage { get; set; } = 25f;

	/// <summary>
	/// Maximum trace distance for a shot.
	/// </summary>
	[Property]
	[Range( 100f, 10000f )]
	[Step( 100f )]
	public float Range { get; set; } = 5000f;

	/// <summary>
	/// Rounds in a full magazine. Reload refills <see cref="CurrentAmmo"/> to this value.
	/// </summary>
	[Property]
	[Range( 1, 100 )]
	[Step( 1 )]
	public int MagazineSize { get; set; } = 7;

	/// <summary>
	/// Seconds spent in <see cref="WeaponState.Reloading"/> before ammo refills.
	/// </summary>
	[Property]
	[Range( 0.1f, 5f )]
	[Step( 0.1f )]
	public float ReloadDuration { get; set; } = 1.5f;

	[Property] public Vector3 WeaponOffset { get; set; } = Vector3.Zero;
	[Property] public Angles WeaponAngleOffset { get; set; } = Angles.Zero;
	[Property] public Vector3 WeaponScale { get; set; } = Vector3.One;

	/// <summary>
	/// The firing state machine. Transitions:
	/// <list type="bullet">
	/// <item><c>Idle → Reloading</c> on reload input (if mag isn't full)</item>
	/// <item><c>Idle → Empty</c> after firing the last round</item>
	/// <item><c>Empty → Reloading</c> on reload input</item>
	/// <item><c>Reloading → Idle</c> when the reload timer expires (mag refilled)</item>
	/// </list>
	/// Firing is only permitted in <c>Idle</c>.
	/// </summary>
	public enum WeaponState
	{
		Idle,
		Reloading,
		Empty,
	}

	private GameObject Weapon => Equipment?.Equipped;
	private bool _holstered;
	private WeaponState _state = WeaponState.Idle;
	private int _currentAmmo;
	private float _reloadEndTime;

	/// <summary>
	/// True when the weapon has been manually holstered by the player. Read-only — toggle
	/// via the in-game holster input. Subscribe to <see cref="IHolsterChangedListener"/>
	/// to react to changes.
	/// </summary>
	public bool IsHolstered => _holstered;

	/// <summary>Current state machine value. Subscribe to <see cref="IWeaponStateChangedListener"/>.</summary>
	public WeaponState State => _state;

	/// <summary>Rounds currently in the magazine.</summary>
	public int CurrentAmmo => _currentAmmo;

	/// <summary>Fraction of reload completed in [0..1]. 0 outside <see cref="WeaponState.Reloading"/>.</summary>
	public float ReloadProgress
	{
		get
		{
			if ( _state != WeaponState.Reloading || ReloadDuration <= 0f ) return 0f;
			var remaining = _reloadEndTime - Time.Now;
			return float.Clamp( 1f - (remaining / ReloadDuration), 0f, 1f );
		}
	}

	protected override void OnStart()
	{
		Equipment ??= Components.Get<Equipment>();
		BodyRenderer ??= Components.Get<SkinnedModelRenderer>();
		AnimHelper ??= Components.Get<CitizenAnimationHelper>();
		_uiState = Components.GetInAncestorsOrSelf<UIStateManager>();
	}

	/// <summary>
	/// Called by <see cref="Equipment.Equip"/> after weapon stats are copied. Resets
	/// the state machine to <see cref="WeaponState.Idle"/> with a full magazine and
	/// broadcasts so the HUD can refresh.
	/// </summary>
	public void OnEquipped()
	{
		_currentAmmo = MagazineSize;
		_reloadEndTime = 0f;
		TransitionTo( WeaponState.Idle );
	}

	protected override void OnUpdate()
	{
		if ( AnimHelper is null ) return;

		ApplyWeaponOffset();

		// Suspend all weapon input (fire/aim/holster/reload) while a UI mode owns the
		// cursor — otherwise inventory clicks would also fire the held weapon.
		if ( _uiState?.IsAnyUIOpen == true )
		{
			AnimHelper.HoldType = CitizenAnimationHelper.HoldTypes.None;
			return;
		}

		if ( Input.Pressed( "Slot1" ) && Weapon.IsValid() )
		{
			_holstered = !_holstered;
			Weapon.Enabled = !_holstered;
			Scene.RunEvent<IHolsterChangedListener>( l => l.OnHolsterChanged( this, _holstered ) );
		}

		var hasWeapon = Weapon.IsValid();
		var aiming = Input.Down( "Attack2" ) && !_holstered && hasWeapon;

		if ( aiming && _state != WeaponState.Reloading )
		{
			AnimHelper.HoldType = HoldType;
			AnimHelper.Handedness = Handedness;
			AnimHelper.IsWeaponLowered = false;
		}
		else
		{
			AnimHelper.HoldType = CitizenAnimationHelper.HoldTypes.None;
		}

		// State machine tick — each state owns its own per-frame behavior and which
		// inputs it cares about. New states should add a case here.
		switch ( _state )
		{
			case WeaponState.Idle:
				if ( aiming && Input.Pressed( "Attack1" ) )
				{
					Fire();
				}
				else if ( Input.Pressed( "Reload" ) && hasWeapon && !_holstered && _currentAmmo < MagazineSize )
				{
					StartReload();
				}
				break;

			case WeaponState.Reloading:
				if ( Time.Now >= _reloadEndTime )
				{
					CompleteReload();
				}
				break;

			case WeaponState.Empty:
				if ( Input.Pressed( "Reload" ) && hasWeapon && !_holstered )
				{
					StartReload();
				}
				// Attack1 in Empty: future hook for a "click" sound / animation.
				break;
		}
	}

	private void TransitionTo( WeaponState newState )
	{
		if ( _state == newState )
		{
			// Re-broadcast even on no-op so listeners get ammo updates from Fire().
			Scene.RunEvent<IWeaponStateChangedListener>( l => l.OnWeaponStateChanged( this ) );
			return;
		}

		_state = newState;
		Scene.RunEvent<IWeaponStateChangedListener>( l => l.OnWeaponStateChanged( this ) );
	}

	private void StartReload()
	{
		_reloadEndTime = Time.Now + ReloadDuration;
		AnimHelper.IsWeaponLowered = true;
		TransitionTo( WeaponState.Reloading );
		Log.Info( $"Reloading ({ReloadDuration:0.0}s)" );
	}

	private void CompleteReload()
	{
		_currentAmmo = MagazineSize;
		_reloadEndTime = 0f;
		TransitionTo( WeaponState.Idle );
		Log.Info( $"Reloaded — {_currentAmmo}/{MagazineSize}" );
	}

	private void ApplyWeaponOffset()
	{
		if ( !Weapon.IsValid() ) return;
		Weapon.LocalPosition = WeaponOffset;
		Weapon.LocalRotation = Rotation.From( WeaponAngleOffset );
		Weapon.LocalScale = WeaponScale;
	}

	private void Fire()
	{
		var camera = Scene.Camera;
		if ( !camera.IsValid() ) return;

		_currentAmmo--;

		var startPos = camera.WorldPosition;
		var endPos = startPos + camera.WorldRotation.Forward * Range;

		var trace = Scene.Trace.Ray( startPos, endPos )
			.IgnoreGameObjectHierarchy( GameObject.Root )
			.Run();

		if ( trace.Hit )
		{
			Log.Info( $"Shot hit {trace.GameObject?.Name}" );
			SpawnDebugMarker( trace.HitPosition, Color.Red );

			var hit = trace.GameObject?.Components.GetInAncestorsOrSelf<Component.IDamageable>();
			if ( hit is not null )
			{
				var info = new DamageInfo
				{
					Damage = Damage,
					Position = trace.HitPosition,
					Origin = startPos,
					Attacker = GameObject.Root,
					Weapon = Weapon,
				};

				CombatSystem.Current.DealDamage( hit, info );
			}
		}
		else
		{
			Log.Info( "Shot - missed" );
			SpawnDebugMarker( endPos, Color.Yellow );
		}

		// Always transition (even on hit/miss) so the HUD reflects the new ammo count.
		// If the magazine just emptied, transition into Empty; otherwise re-broadcast Idle.
		if ( _currentAmmo <= 0 )
		{
			TransitionTo( WeaponState.Empty );
		}
		else
		{
			TransitionTo( WeaponState.Idle );
		}
	}

	private void SpawnDebugMarker( Vector3 position, Color color )
	{
		var go = new GameObject();
		go.Name = "ShotDebug";
		go.WorldPosition = position;
		go.WorldScale = Vector3.One * 0.1f;

		var renderer = go.Components.Create<ModelRenderer>();
		renderer.Model = Model.Load( "models/dev/sphere.vmdl" );
		renderer.Tint = color;

		var marker = go.Components.Create<DebugMarker>();
		marker.Lifetime = 2f;
	}
}
