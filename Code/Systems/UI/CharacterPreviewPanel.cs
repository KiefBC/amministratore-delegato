using Sandbox.UI;

namespace Sandbox;

/// <summary>
/// UI-only 3D preview for the inventory screen. ScenePanel now expects an actual
/// Scene, so this owns a tiny offscreen scene with normal components.
/// </summary>
public sealed class CharacterPreviewPanel : ScenePanel
{
	private readonly Scene _previewScene;
	private readonly CameraComponent _camera;
	private readonly SkinnedModelRenderer _previewRenderer;
	private readonly GameObject _modelObject;

	private Model _shownModel;
	private ulong _shownBodyGroups;
	private string _shownMaterialGroup;
	private Color _shownTint;
	private float _yaw;

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

		var renderer = SourcePlayer?.Components.GetInDescendantsOrSelf<SkinnedModelRenderer>();
		if ( !renderer.IsValid() || renderer.Model is null )
		{
			_modelObject.Enabled = false;
			return;
		}

		_modelObject.Enabled = true;
		UpdatePreviewModel( renderer );
		UpdatePreviewTransform();

		RenderNextFrame();
	}

	public override void Delete( bool immediate = false )
	{
		_previewScene?.Destroy();
		base.Delete( immediate );
	}

	private CameraComponent CreateCamera()
	{
		var cameraObject = _previewScene.CreateObject( true );
		cameraObject.Name = "Preview Camera";
		cameraObject.WorldPosition = new Vector3( 108f, -186f, 78f );
		cameraObject.WorldRotation = Rotation.LookAt( new Vector3( 0f, 0f, 48f ) - cameraObject.WorldPosition );

		var camera = cameraObject.Components.Create<CameraComponent>();
		camera.IsMainCamera = true;
		camera.Priority = 1000;
		camera.FieldOfView = 28f;
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
		modelObject.WorldRotation = Rotation.FromYaw( 158f );
		modelObject.WorldScale = Vector3.One;
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
			_previewRenderer.Model = renderer.Model;
			_shownModel = renderer.Model;
			_shownBodyGroups = ulong.MaxValue;
			_shownMaterialGroup = null;
			_shownTint = default;
		}

		if ( _shownBodyGroups != renderer.BodyGroups )
		{
			_previewRenderer.BodyGroups = renderer.BodyGroups;
			_shownBodyGroups = renderer.BodyGroups;
		}

		if ( _shownMaterialGroup != renderer.MaterialGroup )
		{
			_previewRenderer.MaterialGroup = renderer.MaterialGroup;
			_shownMaterialGroup = renderer.MaterialGroup;
		}

		if ( _shownTint != renderer.Tint )
		{
			_previewRenderer.Tint = renderer.Tint;
			_shownTint = renderer.Tint;
		}

	}

	private void UpdatePreviewTransform()
	{
		_yaw += Time.Delta * 10f;
		_modelObject.WorldRotation = Rotation.FromYaw( 158f + _yaw );
	}
}
