import { readSteamId, validateSboxAuth } from "../_shared/auth.ts";
import { errorResponse, jsonResponse, readJsonObject, readString } from "../_shared/http.ts";
import { createAdminClient } from "../_shared/supabase.ts";

Deno.serve( async ( req ) => {
	if ( req.method !== "POST" ) return errorResponse( "Method not allowed.", 405 );

	const payload = await readJsonObject( req );
	const steamId = readSteamId( payload );
	const auth = await validateSboxAuth( req, steamId );
	if ( !auth.ok ) return errorResponse( auth.message, auth.status );

	const displayName = readString( payload, "display_name", "displayName" ) || "Player";
	const sessionId = readString( payload, "session_id", "sessionId" );
	const action = readString( payload, "action" );
	const sourceAccount = readString( payload, "source_account", "sourceAccount" );
	const destinationAccount = readString( payload, "destination_account", "destinationAccount" );
	const recipientSteamId = readString( payload, "recipient_steam_id", "recipientSteamId" );
	const amountValue = payload.amount;
	const amount = typeof amountValue === "number" && Number.isSafeInteger( amountValue )
		? amountValue
		: Number.parseInt( String( amountValue ?? "" ), 10 );

	if ( !sessionId ) return errorResponse( "Missing session_id." );
	if ( !action ) return errorResponse( "Missing action." );
	if ( !Number.isSafeInteger( amount ) || amount <= 0 ) return errorResponse( "Invalid amount." );

	const supabase = createAdminClient();
	const { data, error } = await supabase
		.rpc( "finance_action", {
			p_steam_id: steamId,
			p_display_name: displayName,
			p_session_id: sessionId,
			p_action: action,
			p_amount: amount,
			p_source_account: sourceAccount || null,
			p_destination_account: destinationAccount || null,
			p_recipient_steam_id: recipientSteamId || null,
		} )
		.single();

	if ( error ) return errorResponse( error.message, 400 );

	const row = data as Record<string, unknown>;
	return jsonResponse( {
		ok: true,
		bank_balance: row.bank_balance,
		debt_balance: row.debt_balance,
		next_debt_accrual_at: row.next_debt_accrual_at_unix,
		wallet_delta: row.wallet_delta,
		recipient_steam_id: row.recipient_steam_id,
		recipient_bank_balance: row.recipient_bank_balance,
		message: row.message,
	} );
} );
