using Sandbox;
using System.Collections.Generic;

/// <summary>
/// Multi-slot bag storage for the player. Distinct from <see cref="Equipment"/>:
/// Equipment owns the one item parented to <c>hold_R</c>; Backpack owns the grid
/// of all items the player carries (the equipped item is *also* tracked here, so
/// the bag UI reflects everything the player owns).
///
/// Items are <see cref="BaseItem"/> Components on world GameObjects. On pickup,
/// the world GameObject is parented under this Backpack's GameObject with renderer
/// and colliders disabled — the same Component instance survives the trip, so
/// per-prefab metadata (Value, Weight, Rarity, weapon stats) stays attached.
///
/// Lives on the player Body GameObject alongside <see cref="Equipment"/> and
/// <see cref="WeaponBehavior"/>.
/// </summary>
public sealed class Backpack : Component
{
	[Property] public int Rows { get; set; } = 4;
	[Property] public int Cols { get; set; } = 6;

	/// <summary>
	/// Optional prefab spawned when the player drops money out of the grid. Should
	/// have a <see cref="MoneyPickup"/> Component plus visuals. If null, drops
	/// fall back to a bare GameObject with a MoneyPickup Component (no visual).
	/// </summary>
	[Property] public GameObject MoneyPickupPrefab { get; set; }

	private BaseItem[,] _slots;
	private (int row, int col)? _selected;
	private int _lastMoneyAmount = -1;

	/// <summary>
	/// Persistent money item that mirrors <see cref="EconomySystem.Money"/>. Created
	/// when balance first goes positive, destroyed when balance hits zero. Never
	/// auto-moves position — sort/move/drag operates on it like any other slot.
	/// </summary>
	private MoneyPickup _moneyItem;

	public BaseItem[,] Slots => _slots;
	public (int row, int col)? Selected => _selected;

	public enum SortMode
	{
		New,
		Worth,
		Weight,
	}

	protected override void OnAwake()
	{
		_slots = new BaseItem[Rows, Cols];
	}

	protected override void OnStart()
	{
		// Sync the money slot if balance is already non-zero at scene start.
		var initial = EconomySystem.Current?.Money ?? 0;
		_lastMoneyAmount = initial;
		if ( initial > 0 )
		{
			SyncMoneySlot( initial );
		}
	}

	protected override void OnUpdate()
	{
		var current = EconomySystem.Current?.Money ?? 0;
		if ( current == _lastMoneyAmount ) return;

		_lastMoneyAmount = current;
		SyncMoneySlot( current );
	}

	// -------------------------------------------------------------------
	// Add / store / remove
	// -------------------------------------------------------------------

	/// <summary>
	/// Add a fresh world item to the bag. Tries to stack into an existing matching
	/// slot first; otherwise places into the first empty slot. Returns false if the
	/// bag is full.
	/// </summary>
	public bool TryAdd( BaseItem item )
	{
		if ( !item.IsValid() ) return false;

		var stackPos = FindStackable( item );
		if ( stackPos is { } sp )
		{
			_slots[sp.r, sp.c].StackCount += item.StackCount;
			item.GameObject.Destroy();
			MarkDirty();
			return true;
		}

		var emptyPos = FirstEmpty();
		if ( emptyPos is null ) return false;

		PlaceInSlot( item, emptyPos.Value.r, emptyPos.Value.c, freshAcquire: true );
		MarkDirty();
		return true;
	}

	/// <summary>
	/// Place an already-tracked bag item into its slot after returning from the hand
	/// bone (e.g., the user equipped a different weapon). Preserves AcquiredTime so
	/// "New" sort doesn't bubble re-equipped items to the front.
	///
	/// If the item already occupies a slot (which is normal — bag entries persist
	/// across equip/unequip), this is a no-op except for re-parenting and renderer
	/// state. If for some reason it isn't tracked, we add it to the first empty slot.
	/// </summary>
	public bool StoreFromHand( BaseItem item )
	{
		if ( !item.IsValid() ) return false;

		ParentToBag( item );

		// Already tracked — nothing else to do, the slot already references this item.
		if ( FindSlot( item ) is not null )
		{
			MarkDirty();
			return true;
		}

		var emptyPos = FirstEmpty();
		if ( emptyPos is null ) return false;

		PlaceInSlot( item, emptyPos.Value.r, emptyPos.Value.c, freshAcquire: false );
		MarkDirty();
		return true;
	}

	/// <summary>
	/// Used by <see cref="WeaponPickup.Interact"/> on the auto-equip-first-weapon path:
	/// the item is now parented to the hand bone, but we still record a bag slot for
	/// it so the inventory UI shows everything the player owns.
	/// </summary>
	public bool TrackEquipped( BaseItem item )
	{
		if ( !item.IsValid() ) return false;

		var emptyPos = FirstEmpty();
		if ( emptyPos is null ) return false;

		// Don't re-parent — Equipment already attached the GO to the hand bone.
		_slots[emptyPos.Value.r, emptyPos.Value.c] = item;
		item.AcquiredTime = 0f;
		MarkDirty();
		return true;
	}

	/// <summary>
	/// Clear a slot and destroy the item. UI / drop / consume call this; do not call
	/// it for "the item moved to hand" — that's a parenting change, not a removal.
	/// </summary>
	public void Remove( int row, int col )
	{
		if ( !InBounds( row, col ) ) return;
		var item = _slots[row, col];
		if ( !item.IsValid() ) return;

		_slots[row, col] = null;
		if ( _moneyItem == item ) _moneyItem = null;
		item.GameObject.Destroy();
		MarkDirty();
	}

	// -------------------------------------------------------------------
	// Move / drag-drop
	// -------------------------------------------------------------------

	/// <summary>
	/// Swap two slots. If the destination is empty, this is a move; if it holds a
	/// stack-compatible item, contents merge into one. Returns false if either slot
	/// index is out of bounds or the source is empty.
	/// </summary>
	public bool MoveSlot( int fromRow, int fromCol, int toRow, int toCol )
	{
		if ( !InBounds( fromRow, fromCol ) ) return false;
		if ( !InBounds( toRow, toCol ) ) return false;
		if ( fromRow == toRow && fromCol == toCol ) return false;

		var src = _slots[fromRow, fromCol];
		if ( !src.IsValid() ) return false;

		var dst = _slots[toRow, toCol];
		if ( !dst.IsValid() )
		{
			_slots[toRow, toCol] = src;
			_slots[fromRow, fromCol] = null;
			MarkDirty();
			return true;
		}

		if ( dst.CanStackWith( src ) )
		{
			dst.StackCount += src.StackCount;
			_slots[fromRow, fromCol] = null;
			src.GameObject.Destroy();
			MarkDirty();
			return true;
		}

		_slots[fromRow, fromCol] = dst;
		_slots[toRow, toCol] = src;
		MarkDirty();
		return true;
	}

	/// <summary>
	/// Drop a slot's item into the world at the player's feet. Money decrements the
	/// balance and spawns a world MoneyPickup of that value; weapons re-enable their
	/// renderer/colliders and become world-pickupable again.
	/// </summary>
	public void DropToWorld( int row, int col )
	{
		if ( !InBounds( row, col ) ) return;
		var item = _slots[row, col];
		if ( !item.IsValid() ) return;

		var spawnPos = GameObject.Root.WorldPosition;

		if ( item is MoneyPickup money )
		{
			var amount = money.StackCount;
			SpawnWorldMoney( amount, spawnPos );
			// The synced economy value changes after the spend; OnUpdate mirrors it
			// into the visible money slot.
			EconomySystem.Current?.TrySpend( amount );
			return;
		}

		// Weapon / generic item drop: unparent to scene, re-enable visuals and colliders.
		// If this item is the currently-held weapon, clear the equipment slot first
		// — otherwise WeaponBehavior keeps firing a phantom weapon.
		var equipment = Components.Get<Equipment>();
		if ( equipment.IsValid() && equipment.Equipped == item.GameObject )
		{
			equipment.UnequipWithoutStoring();
		}

		_slots[row, col] = null;
		item.GameObject.SetParent( null, true );
		item.GameObject.WorldPosition = spawnPos;
		item.GameObject.Enabled = true;

		var renderer = item.GameObject.Components.Get<ModelRenderer>();
		if ( renderer.IsValid() ) renderer.Enabled = true;

		foreach ( var col2 in item.GameObject.Components.GetAll<Collider>() )
		{
			col2.Enabled = true;
		}

		MarkDirty();
	}

	// -------------------------------------------------------------------
	// Sort
	// -------------------------------------------------------------------

	/// <summary>
	/// Flatten non-empty slots, sort by mode, refill row-major from (0,0). Stable
	/// secondary key is unused — sort orders are total enough for practical use.
	/// </summary>
	public void Sort( SortMode mode )
	{
		var items = new List<BaseItem>();
		for ( int r = 0; r < Rows; r++ )
		{
			for ( int c = 0; c < Cols; c++ )
			{
				if ( _slots[r, c].IsValid() ) items.Add( _slots[r, c] );
			}
		}

		items.Sort( ( a, b ) => mode switch
		{
			// "New" = newest first → smaller AcquiredTime (less time elapsed) first.
			SortMode.New => ((float)a.AcquiredTime).CompareTo( (float)b.AcquiredTime ),
			// "Worth" = highest total slot value first.
			SortMode.Worth => (b.Value * b.StackCount).CompareTo( a.Value * a.StackCount ),
			// "Weight" = lightest total slot weight first.
			SortMode.Weight => (a.Weight * a.StackCount).CompareTo( b.Weight * b.StackCount ),
			_ => 0,
		} );

		for ( int r = 0; r < Rows; r++ )
			for ( int c = 0; c < Cols; c++ )
				_slots[r, c] = null;

		var idx = 0;
		for ( int r = 0; r < Rows && idx < items.Count; r++ )
		{
			for ( int c = 0; c < Cols && idx < items.Count; c++ )
			{
				_slots[r, c] = items[idx++];
			}
		}

		MarkDirty();
	}

	// -------------------------------------------------------------------
	// Selection
	// -------------------------------------------------------------------

	public void Select( int row, int col )
	{
		if ( !InBounds( row, col ) ) return;
		_selected = (row, col);
		MarkDirty();
	}

	public void ClearSelection()
	{
		if ( _selected is null ) return;
		_selected = null;
		MarkDirty();
	}

	// -------------------------------------------------------------------
	// Money slot — kept in sync with EconomySystem.Money
	// -------------------------------------------------------------------

	private void SyncMoneySlot( int newAmount )
	{
		if ( newAmount > 0 )
		{
			if ( _moneyItem.IsValid() )
			{
				_moneyItem.StackCount = newAmount;
				MarkDirty();
				return;
			}

			var emptyPos = FirstEmpty();
			if ( emptyPos is null ) return; // bag full — money still in EconomySystem, just not visible

			var go = new GameObject();
			go.Name = "Money";
			go.SetParent( GameObject, false );

			var money = go.Components.Create<MoneyPickup>();
			money.StackCount = newAmount;
			_moneyItem = money;

			PlaceInSlot( money, emptyPos.Value.r, emptyPos.Value.c, freshAcquire: true );
			MarkDirty();
		}
		else
		{
			if ( !_moneyItem.IsValid() ) return;

			var pos = FindSlot( _moneyItem );
			if ( pos is { } p ) _slots[p.r, p.c] = null;
			_moneyItem.GameObject.Destroy();
			_moneyItem = null;
			MarkDirty();
		}
	}

	// -------------------------------------------------------------------
	// Internal helpers
	// -------------------------------------------------------------------

	private void PlaceInSlot( BaseItem item, int row, int col, bool freshAcquire )
	{
		ParentToBag( item );
		_slots[row, col] = item;
		if ( freshAcquire ) item.AcquiredTime = 0f;
	}

	private void ParentToBag( BaseItem item )
	{
		item.GameObject.SetParent( GameObject, false );
		item.GameObject.LocalPosition = Vector3.Zero;
		item.GameObject.LocalRotation = Rotation.Identity;
		item.GameObject.Enabled = true;

		var renderer = item.GameObject.Components.Get<ModelRenderer>();
		if ( renderer.IsValid() ) renderer.Enabled = false;

		foreach ( var col in item.GameObject.Components.GetAll<Collider>() )
		{
			col.Enabled = false;
		}
	}

	private (int r, int c)? FirstEmpty()
	{
		for ( int r = 0; r < Rows; r++ )
		{
			for ( int c = 0; c < Cols; c++ )
			{
				if ( !_slots[r, c].IsValid() ) return (r, c);
			}
		}
		return null;
	}

	private (int r, int c)? FindStackable( BaseItem item )
	{
		for ( int r = 0; r < Rows; r++ )
		{
			for ( int c = 0; c < Cols; c++ )
			{
				if ( _slots[r, c].IsValid() && _slots[r, c].CanStackWith( item ) ) return (r, c);
			}
		}
		return null;
	}

	private (int r, int c)? FindSlot( BaseItem item )
	{
		for ( int r = 0; r < Rows; r++ )
		{
			for ( int c = 0; c < Cols; c++ )
			{
				if ( _slots[r, c] == item ) return (r, c);
			}
		}
		return null;
	}

	private bool InBounds( int row, int col )
		=> row >= 0 && row < Rows && col >= 0 && col < Cols;

	private void SpawnWorldMoney( int amount, Vector3 position )
	{
		GameObject go;
		if ( MoneyPickupPrefab.IsValid() )
		{
			go = MoneyPickupPrefab.Clone();
			go.WorldPosition = position;
		}
		else
		{
			go = new GameObject();
			go.Name = $"Money (${amount})";
			go.WorldPosition = position;
		}

		var pickup = go.Components.Get<MoneyPickup>() ?? go.Components.Create<MoneyPickup>();
		pickup.StackCount = amount;
	}

	private void MarkDirty()
	{
		// Inventory UI reads this component through BuildHash; no scene-wide event needed.
	}
}
