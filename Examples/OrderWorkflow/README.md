# Order Workflow example

This sample keeps most business behavior in Spellkit source files. C# provides a small order
ledger, registers three lifecycle signals, and explicitly chooses when queued events are delivered.

The script entry point imports four modules:

- `workflow/model.kit` converts host payloads into a script value type;
- `workflow/validation.kit` accepts or rejects submitted orders;
- `workflow/shipping.kit` chooses a delivery plan;
- `workflow/notifications.kit` formats business-facing messages.

`main.kit` installs handlers for `order.submitted`, `order.payment.confirmed`, and
`order.shipment.requested`. The payment handler emits the shipment signal. Because signal delivery
is explicit, the host calls `DispatchSignalsAsync()` a second time to deliver that newly queued request.

The sample accepts `ORD-1001`, rejects an invalid `ORD-1002`, then confirms payment for the accepted
order. The final output shows host ledger entries alongside the Script-owned `submitted`, `paid`, and
`shipped` counters.

Run it from the repository root:

```powershell
dotnet run --project .\Examples\OrderWorkflow\OrderWorkflow.csproj
```
