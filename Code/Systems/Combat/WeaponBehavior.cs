using Sandbox;
using Sandbox.Citizen;

namespace Sandbox.Systems.Combat;

/// <summary>
/// Local weapon input and animation for the owned player. Authoritative state
/// changes live in <see cref="Backpack"/> on the host and are requested through
/// <see cref="GameNetworkRpc"/>.
/// </summary>
public sealed class WeaponBehavior : Component
{
	[Property] public SkinnedModelRenderer BodyRenderer { get; set; }
	[Property] public CitizenAnimationHelper AnimHelper { get; set; }
	[Property] public Equipment Equipment { get; set; }
	[Property] public Backpack Backpack { get; set; }

	private bool _lastRequestedAiming;

	public enum WeaponState
	{
		Idle,
		Reloading,
		Empty,
	}

	public ItemDefinition Definition { get; private set; }
	public string DisplayName => Definition?.DisplayName;
	public bool IsHolstered { get; private set; }
	public bool IsAiming { get; private set; }
	public WeaponState State { get; private set; } = WeaponState.Idle;
	public int CurrentAmmo { get; private set; }
	public int MagazineSize => Weapon?.ClipSize ?? 0;

	public float ReloadProgress
	{
		get
		{
			var weapon = Weapon;
			if ( State != WeaponState.Reloading || weapon is null || weapon.ReloadDuration <= 0f ) return 0f;

			var remaining = EquippedState.ReloadEndTime - Time.Now;
			return float.Clamp( 1f - (remaining / weapon.ReloadDuration), 0f, 1f );
		}
	}

	private InventoryItemState EquippedState { get; set; }
	private WeaponStats Weapon => Definition?.Weapon;
	private bool HasWeapon => Definition is not null && Definition.IsWeapon;
	private bool IsLocalOwner => Sandbox.Systems.Movement.LocalPlayer.Owns( GameObject.Root );

	protected override void OnStart()
	{
		Equipment ??= Components.Get<Equipment>();
		Backpack ??= Components.Get<Backpack>();
		BodyRenderer ??= Components.Get<SkinnedModelRenderer>();
		AnimHelper ??= Components.Get<CitizenAnimationHelper>();
	}

	protected override void OnUpdate()
	{
		RefreshState();
		ApplyAnimationState();

		if ( !IsLocalOwner ) return;
		if ( UiModeSystem.Current?.IsCursorVisible == true )
		{
			RequestAimingIfChanged( false );
			return;
		}

		PollInput();
	}

	private void RefreshState()
	{
		Equipment ??= Components.Get<Equipment>();
		Backpack ??= Components.Get<Backpack>();

		Definition = null;
		EquippedState = default;
		IsHolstered = false;
		IsAiming = false;
		State = WeaponState.Idle;
		CurrentAmmo = 0;

		if ( !Backpack.IsValid() ) return;
		if ( !Backpack.TryGetEquipped( out var item, out var definition ) ) return;
		if ( definition is null || !definition.IsWeapon ) return;

		Definition = definition;
		EquippedState = item;
		IsHolstered = item.IsHolstered;
		IsAiming = Backpack.IsWeaponAiming;
		State = item.State;
		CurrentAmmo = item.Ammo;
	}

	private void ApplyAnimationState()
	{
		if ( !AnimHelper.IsValid() ) return;
		var weapon = Weapon;

		if ( weapon is null || IsHolstered )
		{
			AnimHelper.HoldType = CitizenAnimationHelper.HoldTypes.None;
			return;
		}

		var aiming = (IsLocalOwner ? Input.Down( "Attack2" ) : IsAiming) && State != WeaponState.Reloading;
		if ( aiming )
		{
			AnimHelper.HoldType = weapon.HoldType;
			AnimHelper.Handedness = weapon.Handedness;
			AnimHelper.IsWeaponLowered = false;
		}
		else
		{
			AnimHelper.HoldType = CitizenAnimationHelper.HoldTypes.None;
		}

		if ( State == WeaponState.Reloading )
		{
			AnimHelper.IsWeaponLowered = true;
		}
	}

	private void PollInput()
	{
		if ( !HasWeapon )
		{
			RequestAimingIfChanged( false );
			return;
		}

		RequestAimingIfChanged( Input.Down( "Attack2" ) && !IsHolstered && State != WeaponState.Reloading );

		if ( Input.Pressed( "Slot1" ) )
		{
			GameNetworkRpc.RequestToggleHolster( GameObject.Root );
		}

		if ( Input.Pressed( "Reload" ) && !IsHolstered )
		{
			GameNetworkRpc.RequestReloadWeapon( GameObject.Root );
		}

		if ( Input.Down( "Attack2" ) && Input.Pressed( "Attack1" ) && !IsHolstered )
		{
			var camera = Scene.Camera;
			if ( camera.IsValid() )
			{
				GameNetworkRpc.RequestFireWeapon( GameObject.Root, camera.WorldPosition, camera.WorldRotation );
			}
		}
	}

	private void RequestAimingIfChanged( bool aiming )
	{
		if ( _lastRequestedAiming == aiming ) return;

		_lastRequestedAiming = aiming;
		GameNetworkRpc.RequestSetWeaponAiming( GameObject.Root, aiming );
	}

	public static void SpawnDebugMarker( Scene scene, Vector3 position, Color color )
	{
		if ( scene is null ) return;

		var go = new GameObject( "ShotDebug" );
		go.WorldPosition = position;
		go.WorldScale = Vector3.One * 0.1f;
		go.NetworkMode = NetworkMode.Never;

		var renderer = go.Components.Create<ModelRenderer>();
		renderer.Model = Model.Load( "models/dev/sphere.vmdl" );
		renderer.Tint = color;

		var marker = go.Components.Create<DebugMarker>();
		marker.Lifetime = 2f;
	}
}
