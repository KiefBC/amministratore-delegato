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

	update public.players as p
	set
		bank_balance = p.bank_balance + v_job.reward_money,
		job_xp = p.job_xp + v_job.reward_xp,
		job_completions = p.job_completions + 1,
		last_job_at = v_now,
		display_name = coalesce(nullif(trim(p_display_name), ''), p.display_name)
	where p.steam_id = p_steam_id
	returning p.* into v_player;

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

revoke all on function public.complete_job(text, text, text, text, text) from public, anon, authenticated;
grant execute on function public.complete_job(text, text, text, text, text) to service_role;
