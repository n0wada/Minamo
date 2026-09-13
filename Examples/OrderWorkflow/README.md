# Order Workflow example

This sample keeps most business behavior in Minamo source files. C# provides a small order ledger
and supplies submitted orders and confirmed payments through the instance environment.

The script entry point imports four modules:

- `workflow/model.nami` converts host payloads into a script value type;
- `workflow/validation.nami` accepts or rejects submitted orders;
- `workflow/shipping.nami` chooses a delivery plan;
- `workflow/notifications.nami` formats business-facing messages.

`main.nami` processes the supplied orders, validates them, and directly advances accepted orders
through payment and shipment processing.

The sample accepts `ORD-1001`, rejects an invalid `ORD-1002`, then confirms payment for the accepted
order. Host commands record the `submitted`, `paid`, and `shipped` counters in the instance
registry. Minamo can read those values, while only C# can update them. The final output shows the
registry counters alongside the host ledger entries.

Run it from the repository root:

```powershell
dotnet run --project .\Examples\OrderWorkflow\OrderWorkflow.csproj
```
