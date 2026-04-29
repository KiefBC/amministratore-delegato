using Sandbox;

public sealed class DebugMarker : Component
{
	/// <summary>
	/// Time in seconds before this marker auto-destroys.
	/// </summary>
	[Property]
	public float Lifetime { get; set; } = 2f;

	protected override void OnUpdate()
	{
		Lifetime -= Time.Delta;
		if ( Lifetime <= 0f )
		{
			GameObject.Destroy();
		}
	}
}
