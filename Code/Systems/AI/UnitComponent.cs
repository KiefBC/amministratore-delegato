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

	/// <summary>
	/// Money paid to the player who lands the killing blow on this unit. 0 = no payout.
	/// Paid by the host from <see cref="Die"/>.
	/// </summary>
	[Property]
	[Range( 0, 1000 )]
	[Step( 10 )]
	public int Bounty { get; set; } = 0;

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
	private GameObject _lastAttacker;
	private bool _bountyPaid;

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

		// Track the last hostile hitter so Die() can credit the kill. Heals (negative
		// damage) shouldn't overwrite — otherwise a friendly heal could rob the killer.
		if ( info.Damage > 0f )
		{
			_lastAttacker = info.Attacker;
		}

		var wasAlive = !IsDead;
		Health = float.Clamp( Health - info.Damage, 0f, MaxHealth );

		if ( wasAlive && IsDead )
		{
			TryPayBounty();
		}
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

		TryPayBounty();
	}

	private void TryPayBounty()
	{
		if ( !Networking.IsHost ) return;
		if ( _bountyPaid ) return;
		if ( Bounty <= 0 )
		{
			Log.Info( $"{Name}: no bounty paid because Bounty is {Bounty}" );
			return;
		}
		if ( !_lastAttacker.IsValid() )
		{
			Log.Warning( $"{Name}: no bounty paid because last attacker is invalid" );
			return;
		}

		var killerBackpack = FindKillerBackpack();
		if ( !killerBackpack.IsValid() )
		{
			Log.Warning( $"{Name}: no bounty paid because attacker {_lastAttacker.Name} has no Backpack" );
			return;
		}

		var killerUnit = killerBackpack.GameObject.Components.GetInAncestorsOrSelf<UnitComponent>();
		if ( !killerUnit.IsValid() ) killerUnit = killerBackpack.GameObject.Components.GetInDescendantsOrSelf<UnitComponent>();
		if ( killerUnit.IsValid() && killerUnit.Team != TeamType.Player )
		{
			Log.Info( $"{Name}: no bounty paid because killer team is {killerUnit.Team}" );
			return;
		}

		var oldWallet = killerBackpack.Wallet;
		_bountyPaid = true;
		killerBackpack.AddMoney( Bounty );
		Log.Info( $"{Name}: paid ${Bounty} bounty to {killerBackpack.GameObject.Name}. Wallet {oldWallet} -> {killerBackpack.Wallet}" );
	}

	private Backpack FindKillerBackpack()
	{
		var backpack = _lastAttacker.Components.GetInDescendantsOrSelf<Backpack>();
		if ( backpack.IsValid() ) return backpack;

		backpack = _lastAttacker.Components.GetInAncestorsOrSelf<Backpack>();
		if ( backpack.IsValid() ) return backpack;

		var root = _lastAttacker.Root;
		if ( root.IsValid() && root != _lastAttacker )
		{
			backpack = root.Components.GetInDescendantsOrSelf<Backpack>();
			if ( backpack.IsValid() ) return backpack;
		}

		return null;
	}
}
