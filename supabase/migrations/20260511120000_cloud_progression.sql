create table if not exists public.players (
	steam_id text primary key,
	display_name text not null default 'Player',
	bank_balance integer not null default 0 check (bank_balance >= 0),
	job_xp integer not null default 0 check (job_xp >= 0),
	job_completions integer not null default 0 check (job_completions >= 0),
	last_job_at timestamptz,
	created_at timestamptz not null default now(),
	updated_at timestamptz not null default now(),
	constraint players_steam_id_digits check (steam_id ~ '^[0-9]{15,20}$')
);

create table if not exists public.jobs (
	job_id text primary key,
	display_name text not null default 'Job',
	reward_money integer not null check (reward_money >= 0),
	reward_xp integer not null check (reward_xp >= 0),
	cooldown_seconds integer not null default 10 check (cooldown_seconds >= 0),
	enabled boolean not null default true,
	created_at timestamptz not null default now(),
	updated_at timestamptz not null default now()
);

create table if not exists public.job_claims (
	claim_id text primary key,
	steam_id text not null references public.players(steam_id) on delete cascade,
	session_id text not null,
	job_id text not null references public.jobs(job_id),
	reward_money integer not null check (reward_money >= 0),
	reward_xp integer not null check (reward_xp >= 0),
	created_at timestamptz not null default now()
);

create table if not exists public.player_ledger (
	id bigserial primary key,
	steam_id text not null references public.players(steam_id) on delete cascade,
	reason text not null,
	delta_bank_balance integer not null default 0,
	delta_job_xp integer not null default 0,
	bank_balance_after integer not null check (bank_balance_after >= 0),
	job_xp_after integer not null check (job_xp_after >= 0),
	job_completions_after integer not null check (job_completions_after >= 0),
	session_id text,
	job_id text,
	claim_id text,
	created_at timestamptz not null default now()
);

create index if not exists job_claims_steam_id_created_at_idx on public.job_claims (steam_id, created_at desc);
create index if not exists job_claims_session_id_idx on public.job_claims (session_id);
create index if not exists player_ledger_steam_id_created_at_idx on public.player_ledger (steam_id, created_at desc);

alter table public.players enable row level security;
alter table public.jobs enable row level security;
alter table public.job_claims enable row level security;
alter table public.player_ledger enable row level security;

create or replace function public.set_updated_at()
returns trigger
language plpgsql
as $$
begin
	new.updated_at = now();
	return new;
end;
$$;

drop trigger if exists set_players_updated_at on public.players;
create trigger set_players_updated_at
before update on public.players
for each row execute function public.set_updated_at();

drop trigger if exists set_jobs_updated_at on public.jobs;
create trigger set_jobs_updated_at
before update on public.jobs
for each row execute function public.set_updated_at();

insert into public.jobs (job_id, display_name, reward_money, reward_xp, cooldown_seconds, enabled)
values ('starter_workstation', 'Starter Workstation', 25, 10, 10, true)
on conflict (job_id) do update set
	display_name = excluded.display_name,
	reward_money = excluded.reward_money,
	reward_xp = excluded.reward_xp,
	cooldown_seconds = excluded.cooldown_seconds,
	enabled = excluded.enabled;

create or replace function public.complete_job(
	p_steam_id text,
	p_display_name text,
	p_session_id text,
	p_job_id text,
	p_claim_id text
)
returns table (
	bank_balance integer,
	job_xp integer,
	job_completions integer,
	last_job_at_unix bigint,
	reward_money integer,
	reward_xp integer,
	message text
)
language plpgsql
security definer
set search_path = public
as $$
declare
	v_job public.jobs%rowtype;
	v_player public.players%rowtype;
	v_claim public.job_claims%rowtype;
	v_now timestamptz := now();
	v_cooldown_until timestamptz;
begin
	if p_steam_id is null or p_steam_id !~ '^[0-9]{15,20}$' then
		raise exception 'INVALID_STEAM_ID';
	end if;

	if nullif(trim(p_session_id), '') is null then
		raise exception 'INVALID_SESSION_ID';
	end if;

	if nullif(trim(p_job_id), '') is null then
		raise exception 'INVALID_JOB_ID';
	end if;

	if nullif(trim(p_claim_id), '') is null then
		raise exception 'INVALID_CLAIM_ID';
	end if;

	select * into v_job
	from public.jobs
	where job_id = p_job_id and enabled = true;

	if not found then
		raise exception 'UNKNOWN_JOB';
	end if;

	insert into public.players (steam_id, display_name)
	values (p_steam_id, coalesce(nullif(trim(p_display_name), ''), 'Player'))
	on conflict (steam_id) do update set
		display_name = coalesce(nullif(trim(excluded.display_name), ''), public.players.display_name)
	returning * into v_player;

	select * into v_player
	from public.players
	where steam_id = p_steam_id
	for update;

	select * into v_claim
	from public.job_claims
	where claim_id = p_claim_id;

	if found then
		return query select
			v_player.bank_balance,
			v_player.job_xp,
			v_player.job_completions,
			coalesce(extract(epoch from v_player.last_job_at)::bigint, 0::bigint),
			0,
			0,
			'Duplicate claim ignored.'::text;
		return;
	end if;

	if v_player.last_job_at is not null and v_job.cooldown_seconds > 0 then
		v_cooldown_until := v_player.last_job_at + make_interval(secs => v_job.cooldown_seconds);
		if v_now < v_cooldown_until then
			return query select
				v_player.bank_balance,
				v_player.job_xp,
				v_player.job_completions,
				coalesce(extract(epoch from v_player.last_job_at)::bigint, 0::bigint),
				0,
				0,
				'Job is cooling down.'::text;
			return;
		end if;
	end if;

	insert into public.job_claims (claim_id, steam_id, session_id, job_id, reward_money, reward_xp, created_at)
	values (p_claim_id, p_steam_id, p_session_id, p_job_id, v_job.reward_money, v_job.reward_xp, v_now)
	on conflict (claim_id) do nothing
	returning * into v_claim;

	if not found then
		return query select
			v_player.bank_balance,
			v_player.job_xp,
			v_player.job_completions,
			coalesce(extract(epoch from v_player.last_job_at)::bigint, 0::bigint),
			0,
			0,
			'Duplicate claim ignored.'::text;
		return;
	end if;

	update public.players
	set
		bank_balance = bank_balance + v_job.reward_money,
		job_xp = job_xp + v_job.reward_xp,
		job_completions = job_completions + 1,
		last_job_at = v_now,
		display_name = coalesce(nullif(trim(p_display_name), ''), display_name)
	where steam_id = p_steam_id
	returning * into v_player;

	insert into public.player_ledger (
		steam_id,
		reason,
		delta_bank_balance,
		delta_job_xp,
		bank_balance_after,
		job_xp_after,
		job_completions_after,
		session_id,
		job_id,
		claim_id
	)
	values (
		p_steam_id,
		'complete_job',
		v_job.reward_money,
		v_job.reward_xp,
		v_player.bank_balance,
		v_player.job_xp,
		v_player.job_completions,
		p_session_id,
		p_job_id,
		p_claim_id
	);

	return query select
		v_player.bank_balance,
		v_player.job_xp,
		v_player.job_completions,
		coalesce(extract(epoch from v_player.last_job_at)::bigint, 0::bigint),
		v_job.reward_money,
		v_job.reward_xp,
		'Job complete.'::text;
end;
$$;

revoke all on table public.players from anon, authenticated;
revoke all on table public.jobs from anon, authenticated;
revoke all on table public.job_claims from anon, authenticated;
revoke all on table public.player_ledger from anon, authenticated;
revoke all on function public.complete_job(text, text, text, text, text) from public, anon, authenticated;

grant select, insert, update on table public.players to service_role;
grant select on table public.jobs to service_role;
grant select, insert on table public.job_claims to service_role;
grant select, insert on table public.player_ledger to service_role;
grant usage, select on sequence public.player_ledger_id_seq to service_role;
grant execute on function public.complete_job(text, text, text, text, text) to service_role;
