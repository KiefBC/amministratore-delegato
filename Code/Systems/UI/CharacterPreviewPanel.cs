using Sandbox.UI;

namespace Sandbox;

/// <summary>
/// UI-only 3D preview for the inventory screen. ScenePanel now expects an actual
/// Scene, so this owns a tiny offscreen scene with normal components.
/// </summary>
public sealed class CharacterPreviewPanel : ScenePanel
{
	private const float PreviewScale = 1.8f;
	private const float RotationSensitivity = 0.35f;
	private const float InitialYaw = -68f;

	private readonly Scene _previewScene;
	private readonly CameraComponent _camera;
	private readonly SkinnedModelRenderer _previewRenderer;
	private readonly GameObject _modelObject;
	private readonly System.Collections.Generic.Dictionary<System.Guid, SkinnedModelRenderer> _clothingRenderers = new();

	private Model _shownModel;
	private ulong _shownBodyGroups;
	private string _shownMaterialGroup;
	private Color _shownTint;
	private float _yaw = InitialYaw;
	private bool _isRotating;
	private Vector2 _lastMousePosition;

	public static bool IsRotatingAnyPreview { get; private set; }
	public GameObject SourcePlayer { get; set; }

	public CharacterPreviewPanel()
	{
		AddClass( "character-preview-panel" );

		_previewScene = new Scene
		{
			WantsSystemScene = false,
		};

		RenderScene = _previewScene;
		RenderOnce = false;

		_camera = CreateCamera();
		_modelObject = CreateModelObject();
		_previewRenderer = _modelObject.Components.Create<SkinnedModelRenderer>();
		_previewRenderer.UseAnimGraph = false;

		CreateLights();
	}

	public override void Tick()
	{
		base.Tick();

		var renderer = FindBodyRenderer();
		if ( !renderer.IsValid() || renderer.Model is null )
		{
			SetRotating( false );
			_modelObject.Enabled = false;
			ClearClothing();
			return;
		}

		_modelObject.Enabled = true;
		UpdatePreviewModel( renderer );
		UpdatePreviewClothing( renderer );
		PollRotationInput();
		UpdatePreviewTransform();

		RenderNextFrame();
	}

	public override void Delete( bool immediate = false )
	{
		SetRotating( false );
		ClearClothing();
		_previewScene?.Destroy();
		base.Delete( immediate );
	}

	private CameraComponent CreateCamera()
	{
		var cameraObject = _previewScene.CreateObject( true );
		cameraObject.Name = "Preview Camera";
		cameraObject.WorldPosition = new Vector3( 118f, -214f, 82f );
		cameraObject.WorldRotation = Rotation.LookAt( new Vector3( 0f, 0f, 54f ) - cameraObject.WorldPosition );

		var camera = cameraObject.Components.Create<CameraComponent>();
		camera.IsMainCamera = true;
		camera.Priority = 1000;
		camera.FieldOfView = 34f;
		camera.ZNear = 2f;
		camera.ZFar = 512f;
		camera.ClearFlags = ClearFlags.All;
		camera.BackgroundColor = Color.Transparent;
		camera.EnablePostProcessing = true;
		return camera;
	}

	private GameObject CreateModelObject()
	{
		var modelObject = _previewScene.CreateObject( true );
		modelObject.Name = "Preview Character";
		modelObject.WorldPosition = Vector3.Zero;
		modelObject.WorldRotation = Rotation.FromYaw( InitialYaw );
		modelObject.WorldScale = Vector3.One * PreviewScale;
		return modelObject;
	}

	private void CreateLights()
	{
		var sunObject = _previewScene.CreateObject( true );
		sunObject.Name = "Preview Key Light";
		sunObject.WorldRotation = Rotation.LookAt( new Vector3( -0.45f, 0.55f, -0.7f ) );
		var sun = sunObject.Components.Create<DirectionalLight>();
		sun.LightColor = new Color( 1.0f, 0.96f, 0.86f );
		sun.SkyColor = new Color( 0.28f, 0.32f, 0.38f );
		sun.Shadows = true;

		CreatePointLight( "Preview Fill Light", new Vector3( -128f, 70f, 88f ), 260f, new Color( 0.38f, 0.56f, 1.0f ) );
		CreatePointLight( "Preview Rim Light", new Vector3( -48f, 118f, 128f ), 220f, new Color( 0.92f, 1.0f, 1.0f ) );
	}

	private void CreatePointLight( string name, Vector3 position, float radius, Color color )
	{
		var lightObject = _previewScene.CreateObject( true );
		lightObject.Name = name;
		lightObject.WorldPosition = position;

		var light = lightObject.Components.Create<PointLight>();
		light.Radius = radius;
		light.LightColor = color;
		light.Shadows = false;
	}

	private void UpdatePreviewModel( SkinnedModelRenderer renderer )
	{
		if ( _shownModel != renderer.Model )
		{
			_shownModel = renderer.Model;
			_shownBodyGroups = ulong.MaxValue;
			_shownMaterialGroup = null;
			_shownTint = default;
		}

		CopyRendererState( renderer, _previewRenderer );
		_shownTint = renderer.Tint;
		_shownBodyGroups = renderer.BodyGroups;
		_shownMaterialGroup = renderer.MaterialGroup;
	}

	private void UpdatePreviewClothing( SkinnedModelRenderer bodyRenderer )
	{
		var seen = new System.Collections.Generic.HashSet<System.Guid>();

		foreach ( var sourceRenderer in SourcePlayer.Components.GetAll<SkinnedModelRenderer>( FindMode.EnabledInSelfAndDescendants ) )
		{
			if ( !sourceRenderer.IsValid() || sourceRenderer == bodyRenderer || sourceRenderer.Model is null )
				continue;

			seen.Add( sourceRenderer.Id );

			if ( !_clothingRenderers.TryGetValue( sourceRenderer.Id, out var previewRenderer ) || !previewRenderer.IsValid() )
			{
				var clothingObject = _previewScene.CreateObject( true );
				clothingObject.Name = $"Preview Clothing - {sourceRenderer.GameObject.Name}";
				clothingObject.SetParent( _modelObject, false );
				clothingObject.LocalPosition = Vector3.Zero;
				clothingObject.LocalRotation = Rotation.Identity;
				clothingObject.LocalScale = Vector3.One;

				previewRenderer = clothingObject.Components.Create<SkinnedModelRenderer>();
				previewRenderer.UseAnimGraph = false;
				_clothingRenderers[sourceRenderer.Id] = previewRenderer;
			}

			CopyRendererState( sourceRenderer, previewRenderer );
			previewRenderer.BoneMergeTarget = _previewRenderer;
		}

		foreach ( var id in new System.Collections.Generic.List<System.Guid>( _clothingRenderers.Keys ) )
		{
			if ( seen.Contains( id ) ) continue;

			var renderer = _clothingRenderers[id];
			if ( renderer.IsValid() )
			{
				renderer.GameObject.Destroy();
			}

			_clothingRenderers.Remove( id );
		}
	}

	private void CopyRendererState( SkinnedModelRenderer source, SkinnedModelRenderer target )
	{
		target.CopyFrom( source );
		target.UseAnimGraph = false;
	}

	private SkinnedModelRenderer FindBodyRenderer()
	{
		var unit = SourcePlayer?.Components.GetInDescendantsOrSelf<UnitComponent>();
		if ( unit.IsValid() && unit.ModelRenderer.IsValid() )
			return unit.ModelRenderer;

		var equipment = SourcePlayer?.Components.GetInDescendantsOrSelf<Equipment>();
		if ( equipment.IsValid() && equipment.BodyRenderer.IsValid() )
			return equipment.BodyRenderer;

		return SourcePlayer?.Components.GetInDescendantsOrSelf<SkinnedModelRenderer>();
	}

	private void ClearClothing()
	{
		foreach ( var renderer in _clothingRenderers.Values )
		{
			if ( renderer.IsValid() )
			{
				renderer.GameObject.Destroy();
			}
		}

		_clothingRenderers.Clear();
	}

	private void UpdatePreviewTransform()
	{
		_modelObject.WorldRotation = Rotation.FromYaw( _yaw );
	}

	private void PollRotationInput()
	{
		var mousePosition = Mouse.Position;

		if ( Input.Pressed( "Attack1" ) && IsInside( mousePosition ) )
		{
			_lastMousePosition = mousePosition;
			SetRotating( true );
		}

		if ( _isRotating && (Input.Released( "Attack1" ) || !Input.Down( "Attack1" )) )
		{
			SetRotating( false );
		}

		if ( !_isRotating ) return;

		var delta = mousePosition - _lastMousePosition;
		_yaw += delta.x * RotationSensitivity;
		_lastMousePosition = mousePosition;
	}

	private void SetRotating( bool rotating )
	{
		if ( _isRotating == rotating ) return;

		_isRotating = rotating;
		IsRotatingAnyPreview = rotating;
		SetMouseCapture( rotating );
	}
}
