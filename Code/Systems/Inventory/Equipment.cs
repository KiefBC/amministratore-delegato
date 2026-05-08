using Sandbox;

namespace Sandbox.Systems.Inventory;

/// <summary>
/// Builds the local equipped-item view from synced inventory state. The object in
/// the player's hand is cosmetic only; <see cref="Backpack"/> is the source of
/// truth for which item is equipped and what its runtime state is.
/// </summary>
public sealed class Equipment : Component
{
	[Property] public SkinnedModelRenderer BodyRenderer { get; set; }
	/// <summary>Fallback bone used only when a weapon item does not define Weapon.HandBone.</summary>
	[Property] public string HandBone { get; set; } = "hold_R";

	private Backpack _backpack;
	private GameObject _viewObject;
	private int _shownInstanceId;
	private string _shownDefinitionPath;

	public InventoryItemState EquippedState { get; private set; }
	public ItemDefinition EquippedDefinition { get; private set; }
	public string EquippedName => EquippedDefinition?.DisplayName;

	protected override void OnStart()
	{
		BodyRenderer ??= Components.Get<SkinnedModelRenderer>();
		_backpack = Components.Get<Backpack>();
		UpdateView();
	}

	protected override void OnUpdate()
	{
		BodyRenderer ??= Components.Get<SkinnedModelRenderer>();
		if ( !_backpack.IsValid() ) _backpack = Components.Get<Backpack>();

		UpdateView();
	}

	protected override void OnDisabled()
	{
		ClearView();
	}

	private void UpdateView()
	{
		EquippedState = default;
		EquippedDefinition = null;

		if ( !_backpack.IsValid() || !_backpack.TryGetEquipped( out var item, out var definition ) )
		{
			ClearView();
			return;
		}

		EquippedState = item;
		EquippedDefinition = definition;

		if ( _viewObject.IsValid()
			&& _shownInstanceId == item.InstanceId
			&& _shownDefinitionPath == item.DefinitionPath )
		{
			ApplyDefinitionTransform( definition );
			_viewObject.Enabled = !item.IsHolstered;
			return;
		}

		ClearView();
		CreateView( item, definition );
	}

	private void CreateView( InventoryItemState item, ItemDefinition definition )
	{
		if ( !BodyRenderer.IsValid() ) return;

		var bone = BodyRenderer.GetBoneObject( GetHandBone( definition ) );
		if ( bone is null ) return;

		var model = ItemDefinition.LoadModel( definition.ModelPath );
		if ( model is null ) return;

		_viewObject = new GameObject( $"Equipped View - {definition.DisplayName}" );
		_viewObject.SetParent( bone, false );
		_viewObject.NetworkMode = NetworkMode.Never;

		var renderer = _viewObject.Components.Create<ModelRenderer>();
		renderer.Model = model;

		_shownInstanceId = item.InstanceId;
		_shownDefinitionPath = item.DefinitionPath;

		ApplyDefinitionTransform( definition );
		_viewObject.Enabled = !item.IsHolstered;
	}

	private void ApplyDefinitionTransform( ItemDefinition definition )
	{
		if ( !_viewObject.IsValid() || definition is null ) return;
		var weapon = definition.Weapon;
		if ( weapon is null ) return;

		_viewObject.LocalPosition = weapon.Offset;
		_viewObject.LocalRotation = Rotation.From( weapon.AngleOffset );
		_viewObject.LocalScale = weapon.Scale;
	}

	private string GetHandBone( ItemDefinition definition )
	{
		var handBone = definition?.Weapon?.HandBone;
		return !string.IsNullOrWhiteSpace( handBone ) ? handBone : HandBone;
	}

	private void ClearView()
	{
		if ( _viewObject.IsValid() )
		{
			_viewObject.Destroy();
		}

		_viewObject = null;
		_shownInstanceId = 0;
		_shownDefinitionPath = null;
	}
}
