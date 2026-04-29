using Sandbox;

public sealed class UnitComponent : Component, Component.IDamageable
{
	/// <summary>
	/// Displayed name
	/// </summary>
	[Property]
	public string Name { get; set; }

	/// <summary>
	/// Which team this unit belongs to
	/// </summary>
	[Property]
	public TeamType Team { get; set; }

	/// <summary>
	/// What the max health is
	/// </summary>
	[Property]
	[Range( 10f, 300f )]
	[Step( 10f )]
	public float MaxHealth { get; set; } = 100f;

	[Property]
	public SkinnedModelRenderer ModelRenderer { get; set; }

	/// <summary>
	/// Optional ModelPhysics component. If assigned, MotionEnabled flips to true on death so the unit ragdolls.
	/// </summary>
	[Property]
	public ModelPhysics Physics { get; set; }

	/// <summary>
	/// Optional PatrolComponent. If assigned, it is disabled on death so the corpse stops walking.
	/// </summary>
	[Property]
	public PatrolComponent Patrol { get; set; }

	/// <summary>
	/// Current health. Host-authoritative — clients receive updates via Sync.
	/// Mutate only through <see cref="OnDamage"/>.
	/// </summary>
	[Sync( SyncFlags.FromHost )]
	public float Health { get; set; }

	private float _lastHealth;

	public bool IsDead => Health <= 0f;

	[Button( "Hurt 10", "success" )]
	public void HurtDebug() => OnDamage( new DamageInfo { Damage = MaxHealth } );

	[Button( "Heal 10", "favorite" )]
	public void HealDebug() => OnDamage( new DamageInfo { Damage = -10f } );

	/// <summary>
	/// Apply damage. Negative <see cref="DamageInfo.Damage"/> heals.
	/// Host-only — proxies wait for the synced <see cref="Health"/> update.
	/// </summary>
	public void OnDamage( in DamageInfo info )
	{
		if ( !Networking.IsHost ) return;
		if ( IsDead ) return;

		Health = float.Clamp( Health - info.Damage, 0f, MaxHealth );
	}

	protected override void OnStart()
	{
		if ( Networking.IsHost )
		{
			Health = MaxHealth;
		}
		_lastHealth = Health;
	}

	protected override void OnUpdate()
	{
		if ( Health == _lastHealth ) return;

		var difference = Health - _lastHealth;
		Log.Info( $"{Name} health changed by {difference:F0}, new health is {Health:F0}" );
		_lastHealth = Health;

		if ( Health <= 0f )
		{
			Die();
		}
	}

	private void Die()
	{
		Log.Info( $"{Name} died" );

		if ( Physics.IsValid() )
		{
			Physics.MotionEnabled = true;
		}
		else
		{
			Log.Warning( $"{Name}: Physics property is not assigned — cannot ragdoll on death" );
		}

		if ( Patrol.IsValid() )
		{
			Patrol.Enabled = false;
		}
	}
}
