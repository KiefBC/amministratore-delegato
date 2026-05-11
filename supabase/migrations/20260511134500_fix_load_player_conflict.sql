create or replace function public.load_player(
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
	last_job_at_unix bigint
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
		coalesce(extract(epoch from v_player.last_job_at)::bigint, 0::bigint);
end;
$$;

revoke all on function public.load_player(text, text) from public, anon, authenticated;
grant execute on function public.load_player(text, text) to service_role;
