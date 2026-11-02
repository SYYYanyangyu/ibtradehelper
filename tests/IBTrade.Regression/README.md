# Strategy regression checks

Run from the repository root:

```sh
dotnet run --project tests/IBTrade.Regression
```

Uses an isolated temporary state directory, fixed clock, failing market-data stub and filled-order stub. Never connects to a broker. Covers timeout and closing-deadline exits without quotes, quote failures and empty responses while holding a position, and fresh/stale/future option quotes. An intentionally absent market-context provider verifies exits do not access entry filters.

These checks verify execution behavior, not strategy profitability. Option reference prices are still minute MIDPOINT bars, not live bid/ask quotes. PnL remains before commissions because the order API does not provide commission reports. Protective exits are market-order requests and remain subject to broker connectivity, rejection, liquidity and polling delay.


## Opt-in research settings

- `RequireRetest`: last completed bar must revisit the opening-range boundary within `RetestTolerancePct`, close back on the breakout side with a directional candle, and have a preceding post-10:00 breakout close within `RetestLookback` bars. Existing VWAP/range filters still apply. Defaults off.
- `UseAtrExits`: simple mean true range of `AtrPeriod` completed bars, multiplied by `AtrStopMultiple` / `AtrTargetMultiple`. Distances are converted to underlying-price percentages and stored on the filled position, so later changes do not move the ATR boundaries. Missing/zero ATR blocks entry. Defaults off.
- `EstimatedCommissionPerContractSide`: adjustable estimate, default $0.65 (not a verified broker tariff). New trades store the estimate at entry and deduct twice the rate times closed contracts. Trade PnL remains gross; estimated net expectancy uses only trades with known estimated costs. Fill PnL already embeds realized execution slippage; do not subtract reported slippage again. Taxes and other fees are not included.

For comparison: keep a baseline with both switches off, then compare retest-only, ATR-only and combined using the same chronological data and costs. Keep evaluation dates separate from parameter selection. These unit scenarios establish rule correctness, not out-of-sample profitability. Historical option bid/ask, Delta and actual commission reporting remain required for realistic contract selection and economic validation. No automatic strategy activation is performed.
