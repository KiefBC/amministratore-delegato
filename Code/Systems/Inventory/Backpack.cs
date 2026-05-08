using Sandbox;
using System.Collections.Generic;
using System.Linq;

namespace Sandbox.Systems.Inventory;

/// <summary>
/// Host-authoritative per-player inventory and wallet. Persistent state is synced
/// as item records; world/equipped GameObjects are views derived from definitions.
/// </summary>
public sealed class Backpack : Component
{
	private const float MaxFireOriginDistance = 128f;
	public const int EquipmentSlotCount = 8;
	public const int ConsumableSlotCount = 4;

	public enum EquipmentSlot
	{
		Head,
		Chest,
		Neck,
		Legs,
		Hands,
		Feet,
		Weapon,
		Offhand,
	}

	[Property] public int Rows { get; set; } = 4;
	[Property] public int Cols { get; set; } = 6;

	[Sync( SyncFlags.FromHost )] public NetDictionary<int, InventoryItemState> Items { get; set; } = new();
	[Sync( SyncFlags.FromHost )] public int EquippedInstanceId { get; set; }
	[Sync( SyncFlags.FromHost )] public int InventoryVersion { get; set; }
	[Sync( SyncFlags.FromHost )] public bool IsWeaponAiming { get; set; }

	private (int row, int col)? _selected;
	private int _nextInstanceId = 1;

	public int SlotCount => Rows * Cols;
	public int TotalSlotCount => SlotCount + EquipmentSlotCount + ConsumableSlotCount;
	public int Wallet => CalculateMoneyBalance();
	public (int row, int col)? Selected => _selected;

	public enum SortMode
	{
		New,
		Type,
		Rarity,
		Worth,
		Weight,
	}

	protected override void OnStart()
	{
		if ( Sandbox.Networking.IsHost )
		{
			EnsureNextInstanceId();
			ApplyEquipmentSlotState( EquippedInstanceId );
		}
	}

	protected override void OnUpdate()
	{
		if ( Sandbox.Networking.IsHost )
		{
			CompleteReloadIfDue();
		}
	}

	public PlayerInventorySaveData CreateSaveData()
	{
		if ( Sandbox.Networking.IsHost ) CompleteReloadIfDue();

		var data = new PlayerInventorySaveData
		{
			EquippedInstanceId = EquippedInstanceId,
		};

		foreach ( var item in Items.Values.Where( x => x.IsValid ).OrderBy( x => x.InstanceId ) )
		{
			data.Items.Add( ToPersistentItemState( item ) );
		}

		return data;
	}

	public void RestoreSaveData( PlayerInventorySaveData data )
	{
		if ( !Sandbox.Networking.IsHost ) return;
		if ( data is null ) return;

		ClearItems();
		EquippedInstanceId = 0;
		IsWeaponAiming = false;
		_nextInstanceId = 1;

		var usedSlots = new HashSet<int>();
		var usedIds = new HashSet<int>();
		foreach ( var savedItem in data.Items?.OrderBy( x => x.InstanceId ) ?? Enumerable.Empty<InventoryItemState>() )
		{
			var item = ToPersistentItemState( savedItem );
			if ( !item.IsValid ) continue;
			if ( !usedIds.Add( item.InstanceId ) ) continue;

			var definition = GetDefinition( item );
			if ( definition is null ) continue;

			item.StackCount = System.Math.Clamp( item.StackCount, 1, int.Max( 1, definition.MaxStack ) );
			item.SlotIndex = RestoredSlotFor( item, usedSlots );
			if ( item.SlotIndex < 0 ) continue;

			usedSlots.Add( item.SlotIndex );
			Items[item.InstanceId] = item;
		}

		EnsureNextInstanceId();
		ApplyEquipmentSlotState( data.EquippedInstanceId );
		Touch();
	}

	public InventoryItemState GetItemAt( int row, int col )
	{
		return GetItemAt( ToSlot( row, col ) );
	}

	public InventoryItemState GetItemAt( int slot )
	{
		foreach ( var item in Items.Values )
		{
			if ( item.IsValid && item.SlotIndex == slot ) return item;
		}

		return default;
	}

	public int EquipmentSlotIndex( EquipmentSlot slot )
	{
		return SlotCount + (int)slot;
	}

	public bool IsGridSlot( int slot )
	{
		return slot >= 0 && slot < SlotCount;
	}

	public bool IsEquipmentSlot( int slot )
	{
		return slot >= SlotCount && slot < SlotCount + EquipmentSlotCount;
	}

	public int ConsumableSlotIndex( int index )
	{
		return SlotCount + EquipmentSlotCount + index;
	}

	public bool IsConsumableSlot( int slot )
	{
		return slot >= SlotCount + EquipmentSlotCount && slot < TotalSlotCount;
	}

	public ItemDefinition GetDefinition( InventoryItemState item )
	{
		return item.IsValid ? ItemDefinition.Resolve( item.DefinitionPath ) : null;
	}

	public bool TryGetEquipped( out InventoryItemState item, out ItemDefinition definition )
	{
		item = default;
		definition = null;

		if ( EquippedInstanceId <= 0 ) return false;
		if ( !Items.TryGetValue( EquippedInstanceId, out item ) || !item.IsValid ) return false;

		definition = GetDefinition( item );
		return definition is not null;
	}

	public bool TryAddDefinition( string definitionPath, int stackCount = 1, int ammo = -1, bool autoEquipFirstWeapon = true )
	{
		if ( !Sandbox.Networking.IsHost ) return false;
		if ( stackCount <= 0 ) return false;

		var definition = ItemDefinition.Resolve( definitionPath );
		if ( definition is null ) return false;

		EnsureNextInstanceId();

		if ( definition.MaxStack > 1 )
		{
			var remaining = stackCount;
			foreach ( var existing in Items.Values.ToList() )
			{
				if ( existing.DefinitionPath != definitionPath ) continue;
				if ( existing.StackCount >= definition.MaxStack ) continue;

				var add = int.Min( remaining, definition.MaxStack - existing.StackCount );
				var updated = existing;
				updated.StackCount += add;
				Items[updated.InstanceId] = updated;
				remaining -= add;
				if ( remaining <= 0 )
				{
					Touch();
					return true;
				}
			}

			stackCount = remaining;
		}

		while ( stackCount > 0 )
		{
			var shouldEquipNewWeapon = autoEquipFirstWeapon
				&& EquippedInstanceId <= 0
				&& definition.IsWeapon
				&& !TryGetItemAt( EquipmentSlotIndex( EquipmentSlot.Weapon ), out _ );
			var slot = shouldEquipNewWeapon ? EquipmentSlotIndex( EquipmentSlot.Weapon ) : FirstEmptySlot();
			if ( slot < 0 ) return false;

			var add = definition.MaxStack > 1 ? int.Min( stackCount, definition.MaxStack ) : 1;
			var instanceId = _nextInstanceId++;
			var state = new InventoryItemState
			{
				InstanceId = instanceId,
				DefinitionPath = definitionPath,
				StackCount = add,
				SlotIndex = slot,
				Ammo = definition.IsWeapon ? (ammo >= 0 ? ammo : definition.Weapon.ClipSize) : 0,
				State = WeaponBehavior.WeaponState.Idle,
				IsHolstered = false,
				ReloadEndTime = 0f,
				LastFireTime = 0f,
			};

			Items[instanceId] = state;
			stackCount -= add;

			if ( shouldEquipNewWeapon )
			{
				EquippedInstanceId = instanceId;
			}
		}

		Touch();
		return true;
	}

	public bool CanAddMoney( int amount )
	{
		if ( amount <= 0 ) return false;
		return CanFitStack( ItemDefinition.MoneyPath, amount );
	}

	public bool AddMoney( int amount )
	{
		if ( amount <= 0 ) return false;
		if ( !Sandbox.Networking.IsHost ) return false;
		if ( !CanAddMoney( amount ) ) return false;

		return TryAddDefinition( ItemDefinition.MoneyPath, amount, autoEquipFirstWeapon: false );
	}

	public bool TrySpend( int amount )
	{
		if ( amount <= 0 ) return false;
		if ( !Sandbox.Networking.IsHost )
		{
			GameNetworkRpc.RequestSpendMoney( GameObject.Root, amount );
			return false;
		}

		if ( Wallet < amount ) return false;

		var remaining = amount;
		foreach ( var item in Items.Values.Where( IsMoneyItem ).OrderBy( x => x.StackCount ).ToList() )
		{
			var spend = int.Min( remaining, item.StackCount );
			if ( spend <= 0 ) continue;

			remaining -= spend;
			var updated = item;
			updated.StackCount -= spend;

			if ( updated.StackCount <= 0 ) RemoveItem( updated.InstanceId );
			else Items[updated.InstanceId] = updated;

			if ( remaining <= 0 ) break;
		}

		if ( remaining > 0 ) return false;

		Touch();
		return true;
	}

	public bool TryUseSlot( int slot )
	{
		if ( !Sandbox.Networking.IsHost ) return false;
		if ( !TryGetItemAt( slot, out var item ) ) return false;

		var definition = GetDefinition( item );
		if ( definition is null ) return false;

		if ( definition.IsWeapon )
		{
			var previousEquipped = EquippedInstanceId;
			var weaponSlot = EquipmentSlotIndex( EquipmentSlot.Weapon );
			if ( item.SlotIndex != weaponSlot && !TryMoveSlotState( item.SlotIndex, weaponSlot ) ) return false;
			if ( !Items.TryGetValue( item.InstanceId, out item ) ) return false;

			IsWeaponAiming = false;
			PrepareEquippedWeapon( ref item );
			Items[item.InstanceId] = item;
			ApplyEquipmentSlotState( previousEquipped );
			Touch();
			return true;
		}

		if ( definition.IsConsumable )
		{
			return TryConsumeItem( item, definition );
		}

		return false;
	}

	private bool TryConsumeItem( InventoryItemState item, ItemDefinition definition )
	{
		var unit = GameObject.Root.Components.GetInDescendantsOrSelf<UnitComponent>();
		if ( !unit.IsValid() ) return false;

		if ( !unit.TryApplyConsumable( definition ) ) return false;

		var consumable = definition.Consumable;
		if ( consumable?.HealthXp > 0f ) PlayerStats()?.AwardHealthFoodXp( consumable.HealthXp );

		var previousStackCount = item.StackCount;
		if ( item.StackCount > 1 )
		{
			item.StackCount--;
			Items[item.InstanceId] = item;
		}
		else
		{
			RemoveItem( item.InstanceId );
		}

		Log.Info( $"[Consumable] {PlayerLogName()} consumed {definition.DisplayName}." );
		GameLogSystem.Current?.Info( "inventory", "Consumable used", GameObject.Root, data: GameLogSystem.Fields(
			("definition", item.DefinitionPath),
			("displayName", definition.DisplayName),
			("remaining", int.Max( 0, previousStackCount - 1 )),
			("health", definition.Consumable?.Health ?? 0f),
			("stamina", definition.Consumable?.Stamina ?? 0f),
			("hydration", definition.Consumable?.Hydration ?? 0f),
			("nutrition", definition.Consumable?.Nutrition ?? 0f) ) );

		GameNetworkRpc.BroadcastPlayerNotification(
			GameObject.Root,
			(int)NotificationKind.Success,
			"Consumed",
			definition.DisplayName,
			2f );

		Touch();
		return true;
	}

	private string PlayerLogName()
	{
		var root = GameObject.Root;
		if ( root.IsValid() && !string.IsNullOrWhiteSpace( root.Name ) ) return root.Name;
		return GameObject.IsValid() && !string.IsNullOrWhiteSpace( GameObject.Name ) ? GameObject.Name : "unknown player";
	}

	public bool TryMoveSlot( int fromSlot, int toSlot )
	{
		if ( !Sandbox.Networking.IsHost ) return false;

		var previousEquipped = EquippedInstanceId;
		if ( !TryMoveSlotState( fromSlot, toSlot ) ) return false;

		ApplyEquipmentSlotState( previousEquipped );
		Touch();
		return true;
	}

	private bool TryMoveSlotState( int fromSlot, int toSlot )
	{
		if ( !InBounds( fromSlot ) || !InBounds( toSlot ) ) return false;
		if ( fromSlot == toSlot ) return false;
		if ( !TryGetItemAt( fromSlot, out var src ) ) return false;
		if ( !CanPlaceItemInSlot( src, toSlot ) ) return false;

		var dstExists = TryGetItemAt( toSlot, out var dst );
		if ( dstExists && !CanPlaceItemInSlot( dst, fromSlot ) ) return false;

		if ( !dstExists )
		{
			src.SlotIndex = toSlot;
			Items[src.InstanceId] = src;
			return true;
		}

		var srcDef = GetDefinition( src );
		if ( srcDef is not null && src.DefinitionPath == dst.DefinitionPath && srcDef.MaxStack > 1 )
		{
			var move = int.Min( src.StackCount, srcDef.MaxStack - dst.StackCount );
			if ( move > 0 )
			{
				dst.StackCount += move;
				src.StackCount -= move;
				Items[dst.InstanceId] = dst;

				if ( src.StackCount <= 0 ) RemoveItem( src.InstanceId );
				else Items[src.InstanceId] = src;

				return true;
			}
		}

		src.SlotIndex = toSlot;
		dst.SlotIndex = fromSlot;
		Items[src.InstanceId] = src;
		Items[dst.InstanceId] = dst;
		return true;
	}

	public bool TryDropSlot( int slot )
	{
		if ( !Sandbox.Networking.IsHost ) return false;
		if ( !TryGetItemAt( slot, out var item ) ) return false;

		var definition = GetDefinition( item );
		if ( definition is null ) return false;

		var previousEquipped = EquippedInstanceId;

		RemoveItem( item.InstanceId );
		ApplyEquipmentSlotState( previousEquipped );
		SpawnWorldPickup( item, definition, GameObject.Root.WorldPosition );
		GameLogSystem.Current?.Info( "inventory", "Inventory item dropped", GameObject.Root, data: GameLogSystem.Fields(
			("definition", item.DefinitionPath),
			("displayName", definition.DisplayName),
			("stackCount", item.StackCount),
			("slot", slot) ) );
		Touch();
		return true;
	}

	public bool TrySort( SortMode mode )
	{
		if ( !Sandbox.Networking.IsHost ) return false;

		var sorted = Items.Values.Where( x => x.IsValid && IsGridSlot( x.SlotIndex ) ).ToList();
		sorted.Sort( ( a, b ) =>
		{
			var ad = GetDefinition( a );
			var bd = GetDefinition( b );
			var primary = mode switch
			{
				SortMode.Type => string.Compare( $"{ad?.Kind} {ad?.DisplayName}", $"{bd?.Kind} {bd?.DisplayName}", System.StringComparison.OrdinalIgnoreCase ),
				SortMode.Rarity => ((int)(bd?.Rarity ?? Rarity.Common)).CompareTo( (int)(ad?.Rarity ?? Rarity.Common) ),
				SortMode.Worth => ((bd?.Value ?? 0) * b.StackCount).CompareTo( (ad?.Value ?? 0) * a.StackCount ),
				SortMode.Weight => ((ad?.Weight ?? 0) * a.StackCount).CompareTo( (bd?.Weight ?? 0) * b.StackCount ),
				_ => a.InstanceId.CompareTo( b.InstanceId ),
			};

			return primary != 0 ? primary : a.InstanceId.CompareTo( b.InstanceId );
		} );

		for ( var i = 0; i < sorted.Count; i++ )
		{
			var item = sorted[i];
			item.SlotIndex = i;
			Items[item.InstanceId] = item;
		}

		Touch();
		return true;
	}

	public bool TryToggleHolster()
	{
		if ( !Sandbox.Networking.IsHost ) return false;
		if ( !TryGetEquipped( out var item, out var definition ) ) return false;
		if ( !definition.IsWeapon ) return false;

		item.IsHolstered = !item.IsHolstered;
		if ( item.IsHolstered ) IsWeaponAiming = false;
		if ( item.IsHolstered && item.State == WeaponBehavior.WeaponState.Reloading )
		{
			item.State = item.Ammo <= 0 ? WeaponBehavior.WeaponState.Empty : WeaponBehavior.WeaponState.Idle;
			item.ReloadEndTime = 0f;
		}

		Items[item.InstanceId] = item;
		Touch();
		return true;
	}

	public bool TryStartReload()
	{
		if ( !Sandbox.Networking.IsHost ) return false;
		if ( !TryGetEquipped( out var item, out var definition ) ) return false;
		if ( !definition.IsWeapon ) return false;
		var weapon = definition.Weapon;
		if ( item.IsHolstered ) return false;
		if ( item.State == WeaponBehavior.WeaponState.Reloading ) return false;
		if ( item.Ammo >= weapon.ClipSize ) return false;

		IsWeaponAiming = false;
		item.State = WeaponBehavior.WeaponState.Reloading;
		item.ReloadEndTime = Time.Now + weapon.ReloadDuration;
		Items[item.InstanceId] = item;
		Touch();
		return true;
	}

	public bool TrySetWeaponAiming( bool aiming )
	{
		if ( !Sandbox.Networking.IsHost ) return false;

		if ( aiming )
		{
			if ( !TryGetEquipped( out var item, out var definition ) ) return false;
			if ( !definition.IsWeapon ) return false;
			if ( item.IsHolstered ) return false;
			if ( item.State == WeaponBehavior.WeaponState.Reloading ) return false;
		}

		if ( IsWeaponAiming == aiming ) return true;

		IsWeaponAiming = aiming;
		Touch( persist: false );
		return true;
	}

	public bool TryFire( Vector3 origin, Rotation aim )
	{
		if ( !Sandbox.Networking.IsHost ) return false;
		if ( !TryGetEquipped( out var item, out var definition ) ) return false;
		if ( !definition.IsWeapon ) return false;
		var weapon = definition.Weapon;

		CompleteReloadIfDue( ref item, definition );
		if ( item.IsHolstered ) return false;
		if ( item.State != WeaponBehavior.WeaponState.Idle ) return false;
		if ( item.Ammo <= 0 )
		{
			item.State = WeaponBehavior.WeaponState.Empty;
			Items[item.InstanceId] = item;
			Touch();
			return false;
		}

		var muzzleOrigin = ValidateFireOrigin( origin );
		var fireAim = ValidateFireAim( aim );
		var stats = PlayerStats();
		if ( Time.Now - item.LastFireTime < weapon.FireInterval ) return false;

		item.Ammo--;
		item.LastFireTime = Time.Now;
		item.State = item.Ammo <= 0 ? WeaponBehavior.WeaponState.Empty : WeaponBehavior.WeaponState.Idle;
		Items[item.InstanceId] = item;
		Touch();

		var fireDirection = ApplyRangedSpread( fireAim, stats );
		var end = muzzleOrigin + fireDirection * weapon.Range;
		var trace = Scene.Trace.Ray( muzzleOrigin, end )
			.Size( Vector3.One * weapon.TraceSize )
			.WithCollisionRules( "bullet" )
			.UseHitboxes( true )
			.IgnoreGameObjectHierarchy( GameObject.Root )
			.Run();

		var hitPosition = trace.Hit ? trace.HitPosition : end;
		GameNetworkRpc.BroadcastShotDebug( GameObject.Root, hitPosition, trace.Hit );

		if ( !trace.Hit )
		{
			Log.Info( $"[Weapon] {PlayerLogName( GameObject.Root )} fired {definition.DisplayName} and missed." );
			stats?.AwardRangedShot( false );
			return true;
		}

		var target = ResolveDamageableTarget( trace.GameObject );
		stats?.AwardRangedShot( target is not null );
		if ( target is null )
		{
			Log.Info( $"[Weapon] {PlayerLogName( GameObject.Root )} fired {definition.DisplayName} and hit {HitObjectLogName( trace.GameObject )}, but no damageable target was found." );
			return true;
		}

		var info = new DamageInfo
		{
			Damage = weapon.Damage,
			Position = trace.HitPosition,
			Origin = muzzleOrigin,
			Attacker = GameObject.Root,
			Weapon = GameObject.Root,
		};

		Log.Info( $"[Weapon] {PlayerLogName( GameObject.Root )} shot {DamageableLogName( target )} with {definition.DisplayName} for {weapon.Damage:0.#} damage." );
		CombatSystem.Current?.DealDamage( target, in info );
		return true;
	}

	private PlayerStatsComponent PlayerStats()
	{
		return GameObject.Root.Components.GetInDescendantsOrSelf<PlayerStatsComponent>();
	}

	private InventoryItemState ToPersistentItemState( InventoryItemState item )
	{
		if ( !item.IsValid ) return default;

		var definition = GetDefinition( item );
		if ( definition is null ) return default;

		item.StackCount = System.Math.Clamp( item.StackCount, 1, int.Max( 1, definition.MaxStack ) );
		item.ReloadEndTime = 0f;
		item.LastFireTime = 0f;

		if ( definition.IsWeapon )
		{
			var clipSize = int.Max( 0, definition.Weapon?.ClipSize ?? item.Ammo );
			item.Ammo = System.Math.Clamp( item.Ammo, 0, clipSize );
			if ( item.State == WeaponBehavior.WeaponState.Reloading )
			{
				item.State = item.Ammo <= 0 ? WeaponBehavior.WeaponState.Empty : WeaponBehavior.WeaponState.Idle;
			}

			return item;
		}

		item.Ammo = 0;
		item.State = WeaponBehavior.WeaponState.Idle;
		item.IsHolstered = false;
		return item;
	}

	private int RestoredSlotFor( InventoryItemState item, HashSet<int> usedSlots )
	{
		if ( InBounds( item.SlotIndex ) && !usedSlots.Contains( item.SlotIndex ) && CanPlaceItemInSlot( item, item.SlotIndex ) )
		{
			return item.SlotIndex;
		}

		return FirstEmptySlot();
	}

	private Vector3 ApplyRangedSpread( Rotation aim, PlayerStatsComponent stats )
	{
		var spreadDegrees = stats.IsValid() ? stats.RangedSpreadDegrees : 0f;
		if ( spreadDegrees <= 0f ) return aim.Forward;

		var spreadAim = aim
			* Rotation.FromPitch( System.Random.Shared.Float( -spreadDegrees, spreadDegrees ) )
			* Rotation.FromYaw( System.Random.Shared.Float( -spreadDegrees, spreadDegrees ) );

		return spreadAim.Forward;
	}

	public void Select( int row, int col )
	{
		if ( !InBounds( ToSlot( row, col ) ) ) return;
		_selected = (row, col);
	}

	public void ClearSelection()
	{
		_selected = null;
	}

	private bool TryGetItemAt( int slot, out InventoryItemState item )
	{
		item = GetItemAt( slot );
		return item.IsValid;
	}

	private bool CanPlaceItemInSlot( InventoryItemState item, int slot )
	{
		var definition = GetDefinition( item );
		if ( !item.IsValid || !InBounds( slot ) ) return false;
		if ( IsGridSlot( slot ) ) return true;
		if ( definition is null ) return false;
		if ( IsConsumableSlot( slot ) ) return definition.IsConsumable;
		if ( !IsEquipmentSlot( slot ) ) return false;

		return EquipmentSlotForIndex( slot ) switch
		{
			EquipmentSlot.Weapon => definition.IsWeapon,
			_ => false,
		};
	}

	private EquipmentSlot? EquipmentSlotForIndex( int slot )
	{
		if ( !IsEquipmentSlot( slot ) ) return null;
		return (EquipmentSlot)(slot - SlotCount);
	}

	private void ApplyEquipmentSlotState( int previousEquippedInstanceId )
	{
		var weaponSlot = EquipmentSlotIndex( EquipmentSlot.Weapon );
		if ( !TryGetItemAt( weaponSlot, out var item ) || GetDefinition( item )?.IsWeapon != true )
		{
			EquippedInstanceId = 0;
			IsWeaponAiming = false;
			return;
		}

		EquippedInstanceId = item.InstanceId;
		if ( previousEquippedInstanceId == item.InstanceId ) return;

		IsWeaponAiming = false;
		PrepareEquippedWeapon( ref item );
		Items[item.InstanceId] = item;
	}

	private void PrepareEquippedWeapon( ref InventoryItemState item )
	{
		item.IsHolstered = false;
		item.ReloadEndTime = 0f;
		item.State = item.Ammo <= 0
			? WeaponBehavior.WeaponState.Empty
			: WeaponBehavior.WeaponState.Idle;
	}

	private void CompleteReloadIfDue()
	{
		if ( !TryGetEquipped( out var item, out var definition ) ) return;
		if ( CompleteReloadIfDue( ref item, definition ) )
		{
			Items[item.InstanceId] = item;
			Touch();
		}
	}

	private bool CompleteReloadIfDue( ref InventoryItemState item, ItemDefinition definition )
	{
		var weapon = definition?.Weapon;
		if ( weapon is null ) return false;
		if ( item.State != WeaponBehavior.WeaponState.Reloading ) return false;
		if ( Time.Now < item.ReloadEndTime ) return false;

		item.Ammo = weapon.ClipSize;
		item.State = WeaponBehavior.WeaponState.Idle;
		item.ReloadEndTime = 0f;
		return true;
	}

	private Vector3 ValidateFireOrigin( Vector3 requestedOrigin )
	{
		var controller = GameObject.Root.Components.GetInDescendantsOrSelf<PlayerController>();
		var fallback = controller.IsValid()
			? controller.EyePosition
			: GameObject.Root.WorldPosition + Vector3.Up * 64f;

		return requestedOrigin.DistanceSquared( fallback ) <= MaxFireOriginDistance * MaxFireOriginDistance ? requestedOrigin : fallback;
	}

	private Rotation ValidateFireAim( Rotation requestedAim )
	{
		return requestedAim;
	}

	private Component.IDamageable ResolveDamageableTarget( GameObject hitObject )
	{
		if ( !hitObject.IsValid() ) return null;

		var target = hitObject.Components.GetInAncestorsOrSelf<Component.IDamageable>();
		if ( target is not null ) return target;

		var controller = hitObject.Components.GetInAncestorsOrSelf<PlayerController>();
		return controller.IsValid()
			? controller.GameObject.Components.GetInDescendantsOrSelf<Component.IDamageable>()
			: null;
	}

	private static string DamageableLogName( Component.IDamageable target )
	{
		if ( target is UnitComponent unit ) return PlayerLogName( unit.GameObject.Root );
		if ( target is Component component ) return PlayerLogName( component.GameObject.Root );
		return "unknown target";
	}

	private static string HitObjectLogName( GameObject hitObject )
	{
		if ( !hitObject.IsValid() ) return "unknown object";
		if ( !string.IsNullOrWhiteSpace( hitObject.Name ) ) return hitObject.Name;
		return "unnamed object";
	}

	private static string PlayerLogName( GameObject player )
	{
		if ( player.IsValid() && !string.IsNullOrWhiteSpace( player.Name ) ) return player.Name;
		return "unknown player";
	}

	private void SpawnWorldPickup( InventoryItemState item, ItemDefinition definition, Vector3 position )
	{
		var go = new GameObject( definition.DisplayName );
		go.WorldPosition = position + Vector3.Up * 12f;
		go.NetworkMode = NetworkMode.Object;
		go.Network.SetOwnerTransfer( OwnerTransfer.Fixed );

		var model = ItemDefinition.LoadModel( definition.WorldModel );
		if ( model is not null )
		{
			var renderer = go.Components.Create<ModelRenderer>();
			renderer.Model = model;
		}

		if ( definition.IsCurrency )
		{
			var money = go.Components.Create<MoneyPickup>();
			money.DefinitionPath = item.DefinitionPath;
			money.Amount = item.StackCount;
		}
		else if ( definition.IsConsumable )
		{
			var pickup = go.Components.Create<FoodPickup>();
			pickup.DefinitionPath = item.DefinitionPath;
			pickup.Amount = item.StackCount;
		}
		else
		{
			var pickup = go.Components.Create<WeaponPickup>();
			pickup.DefinitionPath = item.DefinitionPath;
			pickup.StartingAmmo = item.Ammo;
		}

		go.NetworkSpawn();
	}

	private void RemoveItem( int instanceId )
	{
		Items.Remove( instanceId );
		if ( EquippedInstanceId == instanceId ) EquippedInstanceId = 0;
	}

	private void ClearItems()
	{
		foreach ( var id in Items.Keys.ToList() )
		{
			Items.Remove( id );
		}
	}

	private int CalculateMoneyBalance()
	{
		long total = 0;
		foreach ( var item in Items.Values )
		{
			if ( !IsMoneyItem( item ) ) continue;
			total += int.Max( 0, item.StackCount );
			if ( total >= int.MaxValue ) return int.MaxValue;
		}

		return (int)total;
	}

	private bool CanFitStack( string definitionPath, int stackCount )
	{
		if ( stackCount <= 0 ) return false;

		var definition = ItemDefinition.Resolve( definitionPath );
		if ( definition is null ) return false;

		var maxStack = int.Max( 1, definition.MaxStack );
		var remaining = stackCount;
		foreach ( var existing in Items.Values )
		{
			if ( existing.DefinitionPath != definitionPath ) continue;
			if ( existing.StackCount >= maxStack ) continue;

			remaining -= int.Min( remaining, maxStack - existing.StackCount );
			if ( remaining <= 0 ) return true;
		}

		var neededSlots = (int)System.Math.Ceiling( remaining / (double)maxStack );
		return EmptyGridSlotCount() >= neededSlots;
	}

	private int EmptyGridSlotCount()
	{
		var count = 0;
		for ( var i = 0; i < SlotCount; i++ )
		{
			if ( !TryGetItemAt( i, out _ ) ) count++;
		}

		return count;
	}

	private static bool IsMoneyItem( InventoryItemState item )
	{
		return item.IsValid && item.DefinitionPath == ItemDefinition.MoneyPath;
	}

	private int FirstEmptySlot()
	{
		for ( var i = 0; i < SlotCount; i++ )
		{
			if ( !TryGetItemAt( i, out _ ) ) return i;
		}

		return -1;
	}

	private void EnsureNextInstanceId()
	{
		foreach ( var id in Items.Keys )
		{
			_nextInstanceId = int.Max( _nextInstanceId, id + 1 );
		}
	}

	private int ToSlot( int row, int col )
	{
		return row * Cols + col;
	}

	private bool InBounds( int slot )
	{
		return slot >= 0 && slot < TotalSlotCount;
	}

	private void Touch( bool persist = true )
	{
		InventoryVersion++;
		if ( persist ) PlayerPersistenceSystem.Current?.MarkDirty( GameObject.Root, "inventory changed" );
	}
}
