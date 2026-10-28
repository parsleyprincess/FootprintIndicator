# Genuine MBO integration contract and delivered engine

Updated October 26, 2026. `src/Footprint.Core/MboEngine.cs` implements an optional, independent order-ID engine. **It is not connected to a live NinjaTrader data source.** NinjaTrader's ordinary `OnMarketDepth` callback supplies aggregate price-level quantities; changing a connection to Rithmic or buying an MBO subscription does not establish that this callback exposes exchange order IDs.

The running indicator's ordinary heatmap and studies continue to use MBP and executions. Do not invent order identities from depth positions, quote sizes or timestamps to activate this engine. Neither this engine nor the indicator claims to reproduce Bookmap's proprietary native iceberg/stop detection.

## Delivered capabilities

| Capability | Actual behavior |
| --- | --- |
| Order-ID reconstruction | Validated Add, absolute Replace, Cancel and matched Execute events, with per-instrument sequence continuity and atomic financial accounting |
| Authoritative initialization | Full order snapshot at a known sequence/time boundary; a fresh snapshot is required after loss or inconsistency |
| Individual-order views | Minimum/maximum displayed order quantity, observed age, price/size change filters, percentage of largest eligible orders |
| Aggregate filtered depth | Bid/ask price levels with total quantity, individual-order count, mean/minimum/maximum size |
| Large-lot tracking | Current qualifying orders and a bounded trace of their subsequent changes, fills and cancellation, including moves between prices |
| Same-ID refill candidates | A visible increase at the same price after explicitly matched executions of the same confirmed exchange ID; exact observed refill and execution quantities |
| Resource limits | Bounded live/depleted identities, candidate history and trace points; explicit trim counters; snapshot copies and synchronized mutations |
| Native stop orders / proven hidden quantity | **Not implemented:** requires additional authoritative metadata and documented exchange-specific semantics |
| Live vendor SDK / NinjaTrader bridge | **Not implemented:** requires a genuine feed API exposing the needed information, provider licensing/access, and integration validation |

Sorting is for display: bids descending, asks ascending, then first observation and ordinal ID. It is **not exchange queue priority**. Exchange rules can change priority after replacements, and snapshot arrival order need not be exchange priority.

## Input API

```csharp
var engine = new MboEngine(new MboSettings
{
    MaximumOrders = 100000,
    MaximumRefillCandidates = 10000,
    MaximumTracePoints = 20000,
    RefillWindowMilliseconds = 5000,
    LargeLotMinimumQuantity = 100
});

// Only call this with the complete, authoritative order snapshot supplied
// by a real MBO source. The bridge must buffer all subsequent deltas.
engine.ResetSnapshot(sequenceAtSnapshot, snapshotTime, orders,
    exchangeOrderIdsConfirmed: true);

// These values come from the source/normalizer, not inferred MBP identities.
bool accepted = engine.Apply(new MboEvent(nextSequence, eventTime,
    MboOperation.Execute, exchangeOrderId, restingSide, restingPriceTick,
    executedQuantity));

MboSnapshot view = engine.Snapshot(new MboFilter
{
    MinimumQuantity = 10,
    MaximumQuantity = 1000,
    MinimumAgeMilliseconds = 500,
    KeepLargestPercent = 100,
    Changes = MboChangeFilter.Any
});
```

The snippet illustrates the typed contract; it is not a complete provider connection. All settings are examples, not instrument-calibrated defaults or Bookmap thresholds.

- `PriceTick` is an `Int64` absolute tick-price index. Normalize using the selected instrument's tick size. Negative ticks and fine-price currency futures are supported without a symbol whitelist. Quantities are whole units/contracts in this implementation, consistent with the futures use case; fractional-quantity venues require an explicit quantity-unit conversion.
- `OrderId` is a nonblank string up to 256 characters and must be unique for the current instrument/order lifetime. Namespace genuine provider identities by the necessary instrument/session/channel context. Never substitute a ladder position.
- `Add` creates a new identity with positive displayed quantity. Adding an already-retained identity is an inconsistency.
- `Replace` changes absolute remaining displayed quantity and optionally price. Side remains fixed. Zero quantity is allowed and retains identity. An actual exchange side change must be normalized according to that feed's documented cancel/new-order semantics.
- `Execute` consumes positive displayed remaining quantity of the specified order. It must not exceed the last observed remaining quantity. Side and price identify the resting order; they are not an inferred aggressor classification. Feeds with different refresh/execution ordering must be normalized using their documented semantics rather than applying an execution twice or assuming hidden quantity.
- `Cancel` removes the entire identity and requires quantity zero. Its side/price must match the current order. An explicit new `Add` reusing a canceled ID starts a fresh observation lifetime with no inherited fill evidence.
- Snapshot quantities must be positive. A full snapshot uses a known sequence boundary and must contain every relevant live order, rather than only a subset of the visible prices.

## Sequence, time, resets and failure handling

The engine initially exposes `Synchronized = false` and accepts no deltas. A valid `ResetSnapshot` establishes the sequence, time, book and declared identity capability. It clears earlier histories/counters, advances the engine epoch, and starts each snapshot order's **observed** age at the snapshot time. It cannot recover an order's unprovided pre-snapshot creation time.

Sequences are monotonically contiguous **per instrument in the normalized stream**. A connection-global feed sequence that includes messages for other instruments must not be passed directly. The bridge must first validate the original transport/channel sequence and snapshot boundary, then generate a contiguous instrument sequence. Blindly renumbering a lossy upstream stream would conceal missing data and is unacceptable.

Sequence numbers at or below the last accepted value are ignored and counted as duplicates/old messages. A missing next sequence, backward event timestamp, impossible fill, wrong side/price, unknown identity, capacity breach or arithmetic overflow invalidates the live book. Further deltas are rejected until a new full snapshot arrives. Financial totals do not partially increase on a rejected event. Rejected full snapshots throw without replacing the prior valid state.

Use one consistent ordered event clock. The current contract rejects clock regression instead of guessing how to reorder a provider's differing timestamp channels. A genuine integration must explicitly reconcile the provider's timestamp/sequence guarantees. The logical sequence cannot continue beyond `Int64.MaxValue`; start a new snapshot epoch before that bound.

Call `Invalidate("Provider disconnected")` on transport loss or upstream reset. Live order rows and raw depth are withdrawn immediately. Already confirmed historical candidate/trace records remain historical within that invalidated epoch; they are not evidence of current orders. The next authoritative reset clears them. Always check `Synchronized` before using any live book view.

## Zero remaining quantity and identity budgets

An `Execute` that consumes the full displayed slice leaves an identity with zero displayed quantity. This is intentional: a subsequent explicit `Replace` can represent a refill of the same exchange ID. Such identities are excluded from displayed rows and price levels, but **still count toward `MaximumOrders`**.

The engine does not expire these identities merely because time passed. That would invent a cancellation and could destroy evidence required by the next valid message. A bridge must normalize actual terminal removals/cancels according to the feed's protocol and periodically resynchronize when required. A feed that omits terminal removal messages can exhaust the configured budget; the engine then fails closed and requires a full snapshot. `RetainedOrderIdentities` and `RawDisplayedOrderCount` expose the difference. This behavior must be measured with the actual feed before live deployment.

## Filters and precise meanings

Size and largest-percentage filters operate on **individual remaining order quantities**, then aggregate the surviving orders into price levels. Equal-sized orders at a percentage boundary are all retained, so the result may exceed the requested percentage. Filters change the returned view only: raw bid/ask totals, executions and historical traces are preserved.

Age is elapsed time since first observation of this identity, measured against the latest accepted event time. It does not advance from the computer's wall clock during a stalled stream. `PriceSinceTicks` separately records the latest observed price change. `SizeChanged` and `PriceChanged` describe explicit replacement changes; executions alone do not classify an order as a trader-initiated size replacement. Change history begins at the snapshot boundary.

`LargeOrders` is a view of currently filtered orders meeting the configured individual-order threshold. Once a retained identity has met that threshold, its subsequent events remain in the bounded `LargeLotTrace` even if its remaining quantity later drops below the threshold. Snapshot seeds have no invented pre-snapshot trace; tracking starts with subsequently observed events.

## What a refill candidate does and does not establish

Detection requires `exchangeOrderIdsConfirmed = true`, a matched execution block on that identity, an explicit later replacement increasing visible size, the same resting price, and an unexpired configured window. A replacement consumes the matched execution block so the same fills cannot create multiple candidates. Moving price, unconfirmed IDs, or missing executions cannot qualify.

For bounded state, executions are accumulated into a conservative contiguous observation block. When its first execution is outside the refill window, the block expires rather than counting stale executions because newer ones arrived. This can miss some recent eligible executions around the boundary; lifetime observed execution accounting remains exact. It is not a proprietary iceberg classifier.

The candidate reports matched executed quantity, added visible quantity, visible quantity after replacement, lifetime observed execution and refill number. These are observed values. It reports **no total hidden size, predicted remaining reserve, trader intent, stop quantity, or certainty that the order is an exchange-native iceberg**. Ordinary algorithmic replenishment can produce similar evidence; exchange-specific identity and refresh guarantees need separate validation.

## Validation boundary

`MboTests.Run` exercises initialization, true individual-size filters, observed ages and change semantics, genuine same-ID refill timing, unconfirmed-ID suppression, price-change rejection, causal window expiry, full execution/refresh/cancel/reuse, bounded histories, sequence loss, duplicate messages, malformed events, capacity, transactional `Int64` limits, snapshot isolation, settings validation and concurrent snapshots. A 400-event randomized reference dictionary independently checks retained identities, individual rows, side quantities and execution totals after each event.

These are offline tests of the actual portable engine. No live MBO feed, provider SDK connection, exchange-native iceberg certification or NinjaTrader order-ID callback is validated by them. The Windows adapter must not display these capabilities as live until a genuine bridge is implemented and checked against the provider's own order book and sequence diagnostics.
