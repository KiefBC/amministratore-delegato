using Sandbox;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Host-authoritative per-player inventory and wallet. Persistent state is synced
/// as item records; world/equipped GameObjects are views derived from definitions.
/// </summary>
public sealed class Backpack : Component
{
	[Property] public int Rows { get; set; } = 4;
	[Property] public int Cols { get; set; } = 6;

	[Sync( SyncFlags.FromHost )] public NetDictionary<int, InventoryItemState> Items { get; set; } = new();
	[Sync( SyncFlags.FromHost )] public int EquippedInstanceId { get; set; }
	[Sync( SyncFlags.FromHost )] public int Wallet { get; set; }
	[Sync( SyncFlags.FromHost )] public int InventoryVersion { get; set; }

	private (int row, int col)? _selected;
	private int _nextInstanceId = 1;

	public int SlotCount => Rows * Cols;
	public (int row, int col)? Selected => _selected;

	public enum SortMode
	{
		New,
		Worth,
		Weight,
	}

	protected override void OnStart()
	{
		if ( Networking.IsHost )
		{
			EnsureNextInstanceId();
		}
	}

	protected override void OnUpdate()
	{
		if ( Networking.IsHost )
		{
			CompleteReloadIfDue();
		}
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
		if ( !Networking.IsHost ) return false;
		if ( stackCount <= 0 ) return false;

		var definition = ItemDefinition.Resolve( definitionPath );
		if ( definition is null ) return false;

		if ( definition.IsCurrency )
		{
			AddMoney( stackCount );
			return true;
		}

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
			var slot = FirstEmptySlot();
			if ( slot < 0 ) return false;

			var add = definition.MaxStack > 1 ? int.Min( stackCount, definition.MaxStack ) : 1;
			var instanceId = _nextInstanceId++;
			var state = new InventoryItemState
			{
				InstanceId = instanceId,
				DefinitionPath = definitionPath,
				StackCount = add,
				SlotIndex = slot,
				Ammo = definition.IsWeapon ? (ammo >= 0 ? ammo : definition.MagazineSize) : 0,
				State = WeaponBehavior.WeaponState.Idle,
				IsHolstered = false,
				ReloadEndTime = 0f,
				LastFireTime = 0f,
			};

			Items[instanceId] = state;
			stackCount -= add;

			if ( autoEquipFirstWeapon && EquippedInstanceId <= 0 && definition.IsWeapon )
			{
				EquippedInstanceId = instanceId;
			}
		}

		Touch();
		return true;
	}

	public void AddMoney( int amount )
	{
		if ( amount <= 0 ) return;
		if ( !Networking.IsHost ) return;

		Wallet += amount;
		Touch();
	}

	public bool TrySpend( int amount )
	{
		if ( amount <= 0 ) return false;
		if ( !Networking.IsHost )
		{
			GameNetworkRpc.RequestSpendMoney( GameObject.Root, amount );
			return false;
		}

		if ( Wallet < amount ) return false;
		Wallet -= amount;
		Touch();
		return true;
	}

	public bool TryUseSlot( int slot )
	{
		if ( !Networking.IsHost ) return false;
		if ( !TryGetItemAt( slot, out var item ) ) return false;

		var definition = GetDefinition( item );
		if ( definition is null ) return false;

		if ( definition.IsWeapon )
		{
			EquippedInstanceId = item.InstanceId;
			item.IsHolstered = false;
			if ( item.Ammo <= 0 ) item.State = WeaponBehavior.WeaponState.Empty;
			else item.State = WeaponBehavior.WeaponState.Idle;
			Items[item.InstanceId] = item;
			Touch();
			return true;
		}

		return false;
	}

	public bool TryMoveSlot( int fromSlot, int toSlot )
	{
		if ( !Networking.IsHost ) return false;
		if ( !InBounds( fromSlot ) || !InBounds( toSlot ) ) return false;
		if ( fromSlot == toSlot ) return false;
		if ( !TryGetItemAt( fromSlot, out var src ) ) return false;

		var dstExists = TryGetItemAt( toSlot, out var dst );
		if ( !dstExists )
		{
			src.SlotIndex = toSlot;
			Items[src.InstanceId] = src;
			Touch();
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

				Touch();
				return true;
			}
		}

		src.SlotIndex = toSlot;
		dst.SlotIndex = fromSlot;
		Items[src.InstanceId] = src;
		Items[dst.InstanceId] = dst;
		Touch();
		return true;
	}

	public bool TryDropSlot( int slot )
	{
		if ( !Networking.IsHost ) return false;
		if ( !TryGetItemAt( slot, out var item ) ) return false;

		var definition = GetDefinition( item );
		if ( definition is null ) return false;

		if ( item.InstanceId == EquippedInstanceId )
		{
			EquippedInstanceId = 0;
		}

		RemoveItem( item.InstanceId );
		SpawnWorldPickup( item, definition, GameObject.Root.WorldPosition );
		Touch();
		return true;
	}

	public bool TrySort( SortMode mode )
	{
		if ( !Networking.IsHost ) return false;

		var sorted = Items.Values.Where( x => x.IsValid ).ToList();
		sorted.Sort( ( a, b ) =>
		{
			var ad = GetDefinition( a );
			var bd = GetDefinition( b );
			return mode switch
			{
				SortMode.Worth => ((bd?.Value ?? 0) * b.StackCount).CompareTo( (ad?.Value ?? 0) * a.StackCount ),
				SortMode.Weight => ((ad?.Weight ?? 0) * a.StackCount).CompareTo( (bd?.Weight ?? 0) * b.StackCount ),
				_ => a.InstanceId.CompareTo( b.InstanceId ),
			};
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
		if ( !Networking.IsHost ) return false;
		if ( !TryGetEquipped( out var item, out var definition ) ) return false;
		if ( !definition.IsWeapon ) return false;

		item.IsHolstered = !item.IsHolstered;
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
		if ( !Networking.IsHost ) return false;
		if ( !TryGetEquipped( out var item, out var definition ) ) return false;
		if ( !definition.IsWeapon ) return false;
		if ( item.IsHolstered ) return false;
		if ( item.State == WeaponBehavior.WeaponState.Reloading ) return false;
		if ( item.Ammo >= definition.MagazineSize ) return false;

		item.State = WeaponBehavior.WeaponState.Reloading;
		item.ReloadEndTime = Time.Now + definition.ReloadDuration;
		Items[item.InstanceId] = item;
		Touch();
		return true;
	}

	public bool TryFire( Vector3 origin, Rotation aim )
	{
		if ( !Networking.IsHost ) return false;
		if ( !TryGetEquipped( out var item, out var definition ) ) return false;
		if ( !definition.IsWeapon ) return false;

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
		if ( Time.Now - item.LastFireTime < definition.FireInterval ) return false;

		item.Ammo--;
		item.LastFireTime = Time.Now;
		item.State = item.Ammo <= 0 ? WeaponBehavior.WeaponState.Empty : WeaponBehavior.WeaponState.Idle;
		Items[item.InstanceId] = item;
		Touch();

		var end = muzzleOrigin + aim.Forward * definition.Range;
		var trace = Scene.Trace.Ray( muzzleOrigin, end )
			.WithCollisionRules( "bullet" )
			.IgnoreGameObjectHierarchy( GameObject.Root )
			.Run();

		var hitPosition = trace.Hit ? trace.HitPosition : end;
		GameNetworkRpc.BroadcastShotDebug( GameObject.Root, hitPosition, trace.Hit );

		if ( !trace.Hit ) return true;

		var target = trace.GameObject?.Components.GetInAncestorsOrSelf<Component.IDamageable>();
		if ( target is null ) return true;

		var info = new DamageInfo
		{
			Damage = definition.Damage,
			Position = trace.HitPosition,
			Origin = muzzleOrigin,
			Attacker = GameObject,
			Weapon = GameObject,
		};

		CombatSystem.Current?.DealDamage( target, in info );
		return true;
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
		if ( item.State != WeaponBehavior.WeaponState.Reloading ) return false;
		if ( Time.Now < item.ReloadEndTime ) return false;

		item.Ammo = definition.MagazineSize;
		item.State = WeaponBehavior.WeaponState.Idle;
		item.ReloadEndTime = 0f;
		return true;
	}

	private Vector3 ValidateFireOrigin( Vector3 requestedOrigin )
	{
		var fallback = GameObject.Root.WorldPosition + Vector3.Up * 64f;
		return requestedOrigin.DistanceSquared( fallback ) <= 128f * 128f ? requestedOrigin : fallback;
	}

	private void SpawnWorldPickup( InventoryItemState item, ItemDefinition definition, Vector3 position )
	{
		var go = new GameObject( definition.DisplayName );
		go.WorldPosition = position + Vector3.Up * 12f;
		go.NetworkMode = NetworkMode.Object;

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
		return slot >= 0 && slot < SlotCount;
	}

	private void Touch()
	{
		InventoryVersion++;
	}
}
