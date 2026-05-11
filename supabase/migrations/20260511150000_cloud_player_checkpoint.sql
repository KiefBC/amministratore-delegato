alter table public.players
	add column if not exists player_snapshot jsonb not null default '{}'::jsonb,
	add column if not exists save_version integer not null default 1 check (save_version >= 0),
	add column if not exists last_checkpoint_at timestamptz,
	add column if not exists player_level integer not null default 1 check (player_level >= 0),
	add column if not exists business_level integer not null default 1 check (business_level >= 0),
	add column if not exists businesses_owned integer not null default 0 check (businesses_owned >= 0),
	add column if not exists net_worth bigint not null default 0 check (net_worth >= 0),
	add column if not exists credit_score integer not null default 650 check (credit_score between 300 and 850),
	add column if not exists hours_played_seconds bigint not null default 0 check (hours_played_seconds >= 0);

create table if not exists public.player_checkpoints (
	checkpoint_id text primary key,
	steam_id text not null references public.players(steam_id) on delete cascade,
	session_id text not null,
	reason text not null default 'checkpoint',
	save_version integer not null default 1,
	player_level integer not null default 1,
	business_level integer not null default 1,
	businesses_owned integer not null default 0,
	net_worth bigint not null default 0,
	credit_score integer not null default 650,
	session_seconds_delta integer not null default 0,
	created_at timestamptz not null default now()
);

create index if not exists player_checkpoints_steam_id_created_at_idx on public.player_checkpoints (steam_id, created_at desc);

alter table public.player_checkpoints enable row level security;

drop function if exists public.load_player(text, text);
create function public.load_player(
	p_steam_id text,
	p_display_name text
)
returns table (
	steam_id text,
	display_name text,
	bank_balance integer,
	debt_balance integer,
	next_debt_accrual_at_unix bigint,
	job_xp integer,
	job_completions integer,
	last_job_at_unix bigint,
	player_snapshot jsonb,
	save_version integer,
	last_checkpoint_at_unix bigint,
	player_level integer,
	business_level integer,
	businesses_owned integer,
	net_worth bigint,
	credit_score integer,
	hours_played_seconds bigint
)
language plpgsql
security definer
set search_path = public
as $$
declare
	v_player public.players%rowtype;
begin
	if p_steam_id is null or p_steam_id !~ '^[0-9]{15,20}$' then
		raise exception 'INVALID_STEAM_ID';
	end if;

	insert into public.players (steam_id, display_name)
	values (p_steam_id, coalesce(nullif(trim(p_display_name), ''), 'Player'))
	on conflict on constraint players_pkey do update set
		display_name = coalesce(nullif(trim(excluded.display_name), ''), public.players.display_name);

	v_player := public.accrue_player_debt(p_steam_id);

	return query select
		v_player.steam_id,
		v_player.display_name,
		v_player.bank_balance,
		v_player.debt_balance,
		coalesce(extract(epoch from v_player.next_debt_accrual_at)::bigint, 0::bigint),
		v_player.job_xp,
		v_player.job_completions,
		coalesce(extract(epoch from v_player.last_job_at)::bigint, 0::bigint),
		v_player.player_snapshot,
		v_player.save_version,
		coalesce(extract(epoch from v_player.last_checkpoint_at)::bigint, 0::bigint),
		v_player.player_level,
		v_player.business_level,
		v_player.businesses_owned,
		v_player.net_worth,
		v_player.credit_score,
		v_player.hours_played_seconds;
end;
$$;

drop function if exists public.save_player_checkpoint(text, text, text, text, text, jsonb, integer, integer, integer, bigint, integer, integer);
create function public.save_player_checkpoint(
	p_steam_id text,
	p_display_name text,
	p_session_id text,
	p_checkpoint_id text,
	p_reason text,
	p_snapshot jsonb,
	p_player_level integer,
	p_business_level integer,
	p_businesses_owned integer,
	p_net_worth bigint,
	p_credit_score integer,
	p_session_seconds_delta integer
)
returns table (
	steam_id text,
	display_name text,
	bank_balance integer,
	debt_balance integer,
	next_debt_accrual_at_unix bigint,
	job_xp integer,
	job_completions integer,
	last_job_at_unix bigint,
	player_snapshot jsonb,
	save_version integer,
	last_checkpoint_at_unix bigint,
	player_level integer,
	business_level integer,
	businesses_owned integer,
	net_worth bigint,
	credit_score integer,
	hours_played_seconds bigint,
	message text
)
language plpgsql
security definer
set search_path = public
as $$
declare
	v_player public.players%rowtype;
	v_checkpoint public.player_checkpoints%rowtype;
	v_save_version integer := coalesce(nullif(p_snapshot->>'Version', '')::integer, 1);
	v_reason text := coalesce(nullif(trim(p_reason), ''), 'checkpoint');
begin
	if p_steam_id is null or p_steam_id !~ '^[0-9]{15,20}$' then
		raise exception 'INVALID_STEAM_ID';
	end if;

	if nullif(trim(p_session_id), '') is null then
		raise exception 'INVALID_SESSION_ID';
	end if;

	if nullif(trim(p_checkpoint_id), '') is null then
		raise exception 'INVALID_CHECKPOINT_ID';
	end if;

	if p_snapshot is null or jsonb_typeof(p_snapshot) <> 'object' then
		raise exception 'INVALID_SNAPSHOT';
	end if;

	insert into public.players (steam_id, display_name)
	values (p_steam_id, coalesce(nullif(trim(p_display_name), ''), 'Player'))
	on conflict on constraint players_pkey do update set
		display_name = coalesce(nullif(trim(excluded.display_name), ''), public.players.display_name);

	v_player := public.accrue_player_debt(p_steam_id);

	insert into public.player_checkpoints (
		checkpoint_id,
		steam_id,
		session_id,
		reason,
		save_version,
		player_level,
		business_level,
		businesses_owned,
		net_worth,
		credit_score,
		session_seconds_delta
	)
	values (
		p_checkpoint_id,
		p_steam_id,
		p_session_id,
		v_reason,
		greatest(0, v_save_version),
		greatest(0, coalesce(p_player_level, 1)),
		greatest(0, coalesce(p_business_level, 1)),
		greatest(0, coalesce(p_businesses_owned, 0)),
		greatest(0, coalesce(p_net_worth, 0)),
		least(850, greatest(300, coalesce(p_credit_score, 650))),
		greatest(0, coalesce(p_session_seconds_delta, 0))
	)
	on conflict (checkpoint_id) do nothing
	returning * into v_checkpoint;

	if found then
		update public.players as p
		set
			display_name = coalesce(nullif(trim(p_display_name), ''), p.display_name),
			player_snapshot = p_snapshot,
			save_version = greatest(0, v_save_version),
			last_checkpoint_at = now(),
			player_level = v_checkpoint.player_level,
			business_level = v_checkpoint.business_level,
			businesses_owned = v_checkpoint.businesses_owned,
			net_worth = v_checkpoint.net_worth,
			credit_score = v_checkpoint.credit_score,
			hours_played_seconds = p.hours_played_seconds + v_checkpoint.session_seconds_delta
		where p.steam_id = p_steam_id
		returning p.* into v_player;
	else
		select * into v_player
		from public.players
		where public.players.steam_id = p_steam_id;
	end if;

	insert into public.player_ledger (
		steam_id,
		reason,
		operation,
		delta_bank_balance,
		delta_job_xp,
		delta_debt_balance,
		bank_balance_after,
		job_xp_after,
		job_completions_after,
		debt_balance_after,
		session_id
	)
	values (
		p_steam_id,
		v_reason,
		'checkpoint',
		0,
		0,
		0,
		v_player.bank_balance,
		v_player.job_xp,
		v_player.job_completions,
		v_player.debt_balance,
		p_session_id
	);

	return query select
		v_player.steam_id,
		v_player.display_name,
		v_player.bank_balance,
		v_player.debt_balance,
		coalesce(extract(epoch from v_player.next_debt_accrual_at)::bigint, 0::bigint),
		v_player.job_xp,
		v_player.job_completions,
		coalesce(extract(epoch from v_player.last_job_at)::bigint, 0::bigint),
		v_player.player_snapshot,
		v_player.save_version,
		coalesce(extract(epoch from v_player.last_checkpoint_at)::bigint, 0::bigint),
		v_player.player_level,
		v_player.business_level,
		v_player.businesses_owned,
		v_player.net_worth,
		v_player.credit_score,
		v_player.hours_played_seconds,
		case when v_checkpoint.checkpoint_id is null then 'Duplicate checkpoint ignored.'::text else 'Checkpoint saved.'::text end;
end;
$$;

revoke all on table public.player_checkpoints from anon, authenticated;
revoke all on function public.load_player(text, text) from public, anon, authenticated;
revoke all on function public.save_player_checkpoint(text, text, text, text, text, jsonb, integer, integer, integer, bigint, integer, integer) from public, anon, authenticated;

grant select, insert on table public.player_checkpoints to service_role;
grant execute on function public.load_player(text, text) to service_role;
grant execute on function public.save_player_checkpoint(text, text, text, text, text, jsonb, integer, integer, integer, bigint, integer, integer) to service_role;
