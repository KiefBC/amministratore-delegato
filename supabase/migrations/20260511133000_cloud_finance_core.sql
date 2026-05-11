alter table public.players
	add column if not exists debt_balance integer not null default 0 check (debt_balance >= 0),
	add column if not exists next_debt_accrual_at timestamptz,
	add column if not exists debt_hourly_interest_percent numeric(6,3) not null default 2.0 check (debt_hourly_interest_percent >= 0),
	add column if not exists debt_accrual_interval_seconds integer not null default 3600 check (debt_accrual_interval_seconds > 0);

alter table public.player_ledger
	add column if not exists operation text,
	add column if not exists source_account text,
	add column if not exists destination_account text,
	add column if not exists recipient_steam_id text,
	add column if not exists delta_debt_balance integer not null default 0,
	add column if not exists debt_balance_after integer not null default 0;

create or replace function public.accrue_player_debt(p_steam_id text)
returns public.players
language plpgsql
security definer
set search_path = public
as $$
declare
	v_player public.players%rowtype;
	v_now timestamptz := now();
	v_periods integer := 0;
	v_interest integer := 0;
	v_total_interest integer := 0;
	v_debt integer;
	v_interval integer;
	v_rate numeric;
begin
	select * into v_player
	from public.players
	where steam_id = p_steam_id
	for update;

	if not found then
		raise exception 'UNKNOWN_PLAYER';
	end if;

	if v_player.debt_balance <= 0 then
		if v_player.next_debt_accrual_at is not null then
			update public.players as p
			set next_debt_accrual_at = null
			where p.steam_id = p_steam_id
			returning p.* into v_player;
		end if;

		return v_player;
	end if;

	v_interval := greatest(1, v_player.debt_accrual_interval_seconds);
	v_rate := greatest(0, v_player.debt_hourly_interest_percent);

	if v_rate <= 0 then
		return v_player;
	end if;

	if v_player.next_debt_accrual_at is null then
		update public.players as p
		set next_debt_accrual_at = v_now + make_interval(secs => v_interval)
		where p.steam_id = p_steam_id
		returning p.* into v_player;

		return v_player;
	end if;

	if v_now < v_player.next_debt_accrual_at then
		return v_player;
	end if;

	v_periods := floor(extract(epoch from (v_now - v_player.next_debt_accrual_at)) / v_interval)::integer + 1;
	v_periods := least(greatest(v_periods, 1), 24);
	v_debt := v_player.debt_balance;

	for i in 1..v_periods loop
		v_interest := greatest(1, ceil(v_debt * (v_rate / 100.0))::integer);
		v_debt := least(2147483647, v_debt + v_interest);
		v_total_interest := v_total_interest + v_interest;
	end loop;

	update public.players as p
	set
		debt_balance = v_debt,
		next_debt_accrual_at = greatest(v_now, v_player.next_debt_accrual_at + make_interval(secs => v_interval * v_periods))
	where p.steam_id = p_steam_id
	returning p.* into v_player;

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
		debt_balance_after
	)
	values (
		p_steam_id,
		'debt_interest',
		'debt_interest',
		0,
		0,
		v_total_interest,
		v_player.bank_balance,
		v_player.job_xp,
		v_player.job_completions,
		v_player.debt_balance
	);

	return v_player;
end;
$$;

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
	on conflict (steam_id) do update set
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

drop function if exists public.finance_action(text, text, text, text, integer, text, text, text);
create function public.finance_action(
	p_steam_id text,
	p_display_name text,
	p_session_id text,
	p_action text,
	p_amount integer,
	p_source_account text default null,
	p_destination_account text default null,
	p_recipient_steam_id text default null
)
returns table (
	bank_balance integer,
	debt_balance integer,
	next_debt_accrual_at_unix bigint,
	wallet_delta integer,
	recipient_steam_id text,
	recipient_bank_balance integer,
	message text
)
language plpgsql
security definer
set search_path = public
as $$
declare
	v_player public.players%rowtype;
	v_recipient public.players%rowtype;
	v_action text := lower(coalesce(trim(p_action), ''));
	v_source text := lower(coalesce(trim(p_source_account), ''));
	v_destination text := lower(coalesce(trim(p_destination_account), ''));
	v_amount integer := coalesce(p_amount, 0);
	v_bank_delta integer := 0;
	v_debt_delta integer := 0;
	v_wallet_delta integer := 0;
	v_payment integer := 0;
	v_message text := 'Finance action complete.';
begin
	if p_steam_id is null or p_steam_id !~ '^[0-9]{15,20}$' then
		raise exception 'INVALID_STEAM_ID';
	end if;

	if v_amount <= 0 then
		raise exception 'INVALID_AMOUNT';
	end if;

	if nullif(trim(p_session_id), '') is null then
		raise exception 'INVALID_SESSION_ID';
	end if;

	insert into public.players (steam_id, display_name)
	values (p_steam_id, coalesce(nullif(trim(p_display_name), ''), 'Player'))
	on conflict (steam_id) do update set
		display_name = coalesce(nullif(trim(excluded.display_name), ''), public.players.display_name);

	v_player := public.accrue_player_debt(p_steam_id);

	case v_action
		when 'deposit' then
			v_bank_delta := v_amount;
			v_wallet_delta := -v_amount;

			update public.players as p
			set bank_balance = p.bank_balance + v_amount
			where p.steam_id = p_steam_id
			returning p.* into v_player;

			v_message := 'Deposit complete.';

		when 'withdraw' then
			if v_player.bank_balance < v_amount then
				raise exception 'INSUFFICIENT_BANK_BALANCE';
			end if;

			v_bank_delta := -v_amount;
			v_wallet_delta := v_amount;

			update public.players as p
			set bank_balance = p.bank_balance - v_amount
			where p.steam_id = p_steam_id
			returning p.* into v_player;

			v_message := 'Withdrawal complete.';

		when 'take_loan' then
			if v_destination not in ('wallet', 'bank') then
				raise exception 'INVALID_DESTINATION_ACCOUNT';
			end if;

			if v_player.debt_balance > 2147483647 - v_amount then
				raise exception 'DEBT_BALANCE_TOO_LARGE';
			end if;

			v_debt_delta := v_amount;
			if v_destination = 'bank' then
				v_bank_delta := v_amount;
			else
				v_wallet_delta := v_amount;
			end if;

			update public.players as p
			set
				bank_balance = p.bank_balance + case when v_destination = 'bank' then v_amount else 0 end,
				debt_balance = p.debt_balance + v_amount,
				next_debt_accrual_at = coalesce(p.next_debt_accrual_at, now() + make_interval(secs => greatest(1, p.debt_accrual_interval_seconds)))
			where p.steam_id = p_steam_id
			returning p.* into v_player;

			v_message := 'Loan accepted.';

		when 'repay_debt' then
			if v_source not in ('wallet', 'bank') then
				raise exception 'INVALID_SOURCE_ACCOUNT';
			end if;

			v_payment := least(v_amount, v_player.debt_balance);
			if v_payment <= 0 then
				return query select
					v_player.bank_balance,
					v_player.debt_balance,
					coalesce(extract(epoch from v_player.next_debt_accrual_at)::bigint, 0::bigint),
					0,
					null::text,
					null::integer,
					'No debt to repay.'::text;
				return;
			end if;

			if v_source = 'bank' and v_player.bank_balance < v_payment then
				raise exception 'INSUFFICIENT_BANK_BALANCE';
			end if;

			v_debt_delta := -v_payment;
			if v_source = 'bank' then
				v_bank_delta := -v_payment;
			else
				v_wallet_delta := -v_payment;
			end if;

			update public.players as p
			set
				bank_balance = p.bank_balance - case when v_source = 'bank' then v_payment else 0 end,
				debt_balance = p.debt_balance - v_payment,
				next_debt_accrual_at = case when p.debt_balance - v_payment <= 0 then null else coalesce(p.next_debt_accrual_at, now() + make_interval(secs => greatest(1, p.debt_accrual_interval_seconds))) end
			where p.steam_id = p_steam_id
			returning p.* into v_player;

			v_message := 'Debt repaid.';

		when 'transfer' then
			if v_source not in ('wallet', 'bank') then
				raise exception 'INVALID_SOURCE_ACCOUNT';
			end if;

			if p_recipient_steam_id is null or p_recipient_steam_id !~ '^[0-9]{15,20}$' or p_recipient_steam_id = p_steam_id then
				raise exception 'INVALID_RECIPIENT';
			end if;

			if v_source = 'bank' and v_player.bank_balance < v_amount then
				raise exception 'INSUFFICIENT_BANK_BALANCE';
			end if;

			insert into public.players (steam_id, display_name)
			values (p_recipient_steam_id, 'Player')
			on conflict (steam_id) do nothing;

			if v_source = 'bank' then
				v_bank_delta := -v_amount;

				update public.players as p
				set bank_balance = p.bank_balance - v_amount
				where p.steam_id = p_steam_id
				returning p.* into v_player;
			else
				v_wallet_delta := -v_amount;
			end if;

			update public.players as p
			set bank_balance = p.bank_balance + v_amount
			where p.steam_id = p_recipient_steam_id
			returning p.* into v_recipient;

			insert into public.player_ledger (
				steam_id,
				reason,
				operation,
				source_account,
				destination_account,
				recipient_steam_id,
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
				p_recipient_steam_id,
				'transfer_received',
				'transfer_received',
				v_source,
				'bank',
				p_steam_id,
				v_amount,
				0,
				0,
				v_recipient.bank_balance,
				v_recipient.job_xp,
				v_recipient.job_completions,
				v_recipient.debt_balance,
				p_session_id
			);

			v_message := 'Transfer complete.';

		else
			raise exception 'UNKNOWN_FINANCE_ACTION';
	end case;

	insert into public.player_ledger (
		steam_id,
		reason,
		operation,
		source_account,
		destination_account,
		recipient_steam_id,
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
		v_action,
		v_action,
		nullif(v_source, ''),
		nullif(v_destination, ''),
		p_recipient_steam_id,
		v_bank_delta,
		0,
		v_debt_delta,
		v_player.bank_balance,
		v_player.job_xp,
		v_player.job_completions,
		v_player.debt_balance,
		p_session_id
	);

	return query select
		v_player.bank_balance,
		v_player.debt_balance,
		coalesce(extract(epoch from v_player.next_debt_accrual_at)::bigint, 0::bigint),
		v_wallet_delta,
		case when v_recipient.steam_id is null then null::text else v_recipient.steam_id end,
		case when v_recipient.steam_id is null then null::integer else v_recipient.bank_balance end,
		v_message;
end;
$$;

drop function if exists public.complete_job(text, text, text, text, text);
create function public.complete_job(
	p_steam_id text,
	p_display_name text,
	p_session_id text,
	p_job_id text,
	p_claim_id text
)
returns table (
	bank_balance integer,
	debt_balance integer,
	next_debt_accrual_at_unix bigint,
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
		display_name = coalesce(nullif(trim(excluded.display_name), ''), public.players.display_name);

	v_player := public.accrue_player_debt(p_steam_id);

	select * into v_claim
	from public.job_claims
	where claim_id = p_claim_id;

	if found then
		return query select
			v_player.bank_balance,
			v_player.debt_balance,
			coalesce(extract(epoch from v_player.next_debt_accrual_at)::bigint, 0::bigint),
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
				v_player.debt_balance,
				coalesce(extract(epoch from v_player.next_debt_accrual_at)::bigint, 0::bigint),
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
			v_player.debt_balance,
			coalesce(extract(epoch from v_player.next_debt_accrual_at)::bigint, 0::bigint),
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
		operation,
		delta_bank_balance,
		delta_job_xp,
		delta_debt_balance,
		bank_balance_after,
		job_xp_after,
		job_completions_after,
		debt_balance_after,
		session_id,
		job_id,
		claim_id
	)
	values (
		p_steam_id,
		'complete_job',
		'complete_job',
		v_job.reward_money,
		v_job.reward_xp,
		0,
		v_player.bank_balance,
		v_player.job_xp,
		v_player.job_completions,
		v_player.debt_balance,
		p_session_id,
		p_job_id,
		p_claim_id
	);

	return query select
		v_player.bank_balance,
		v_player.debt_balance,
		coalesce(extract(epoch from v_player.next_debt_accrual_at)::bigint, 0::bigint),
		v_player.job_xp,
		v_player.job_completions,
		coalesce(extract(epoch from v_player.last_job_at)::bigint, 0::bigint),
		v_job.reward_money,
		v_job.reward_xp,
		'Job complete.'::text;
end;
$$;

revoke all on function public.accrue_player_debt(text) from public, anon, authenticated;
revoke all on function public.load_player(text, text) from public, anon, authenticated;
revoke all on function public.finance_action(text, text, text, text, integer, text, text, text) from public, anon, authenticated;
revoke all on function public.complete_job(text, text, text, text, text) from public, anon, authenticated;

grant execute on function public.accrue_player_debt(text) to service_role;
grant execute on function public.load_player(text, text) to service_role;
grant execute on function public.finance_action(text, text, text, text, integer, text, text, text) to service_role;
grant execute on function public.complete_job(text, text, text, text, text) to service_role;
