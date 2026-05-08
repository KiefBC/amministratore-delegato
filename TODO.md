# TODO

- Add a backend market-data provider abstraction for the stock terminal so Alpha Vantage or another keyed service can be used without shipping API keys in client code.

## Phone Book

- Add a wealth/details button to each phone book contact that opens a window or modal showing where that player's net worth is tied up, using high-level totals such as cash/bank, stocks, crypto, businesses, inventory/equipment value, and debt.

## Admin And Persistence

- Add an admin panel for host/admin-only controls such as viewing the server money vault, reviewing debt calculations, changing interest settings, and sending server announcements.
- Add a server-tracked game version checker that compares the running version against the latest trusted release source, such as GitHub releases or s&box metadata, and warns players when a newer version exists or when the running build appears modified/fraudulent.
- Add persistent player/world state instead of session-only state so returning players keep stats, money, inventory/equipment/consumable slots, finance state, debt, businesses, and other important progression after leaving and rejoining.
