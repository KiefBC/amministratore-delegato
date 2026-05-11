# TODO

- Add a backend market-data provider abstraction for the stock terminal so Alpha Vantage or another keyed service can be used without shipping API keys in client code. Issues: https://github.com/KiefBC/amministratore-delegato/issues/2, https://github.com/KiefBC/amministratore-delegato/issues/5
- Add CoinGecko-backed crypto pricing to the stock/finance panel so crypto holdings use live market data alongside stock pricing. Issue: https://github.com/KiefBC/amministratore-delegato/issues/4

## Phone Book

- Add a wealth/details button to each phone book contact that opens a window or modal showing where that player's net worth is tied up, using high-level totals such as cash/bank, stocks, crypto, businesses, inventory/equipment value, and debt. Issues: https://github.com/KiefBC/amministratore-delegato/issues/3, https://github.com/KiefBC/amministratore-delegato/issues/6
- Add player-to-player calling from each player's desk phone, then later extend the same call system to handheld cellphone items. Issues: https://github.com/KiefBC/amministratore-delegato/issues/7, https://github.com/KiefBC/amministratore-delegato/issues/8

## Notifications

- Make notification panels more reactive to their content so each notification only takes up the space its title/message/actions need instead of using a fixed oversized footprint. Issue: https://github.com/KiefBC/amministratore-delegato/issues/9

## Admin And Persistence

- Add an admin panel for host/admin-only controls such as viewing the server money vault, reviewing debt calculations, changing interest settings, and sending server announcements. Issues: https://github.com/KiefBC/amministratore-delegato/issues/11, https://github.com/KiefBC/amministratore-delegato/issues/10, https://github.com/KiefBC/amministratore-delegato/issues/12
- Add a server-tracked game version checker that compares the running version against the latest trusted release source, such as GitHub releases or s&box metadata, and warns players when a newer version exists or when the running build appears modified/fraudulent. Issue: https://github.com/KiefBC/amministratore-delegato/issues/13
- Add persistent world state instead of session-only state so important world/session state survives leaving, rejoining, and host restarts where applicable. Issue: https://github.com/KiefBC/amministratore-delegato/issues/17

## Cloud Persistence And Interfaces

- Standardize cloud/finance UI patterns so ATM, bank, phone, retry/error, loading, and admin panels share consistent modal behavior, styling, loading states, notification handling, and `UiModeSystem` ownership.
- Add an ATM UI for deposits, withdrawals, loans, debt repayment, and transfers.
- Add a Bank UI for account review and future bank/teller actions. Initial version can be read-only, showing bank balance, debt, recent transactions, credit score, and cloud sync state.
- Add a corner cellphone UI with limited initial functionality: player list/contact directory like the desk phone, and read-only Bank UI access. Later expand it into calls, messages, apps, and mobile banking.
