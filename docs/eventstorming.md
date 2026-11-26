# EventStorming board

Big-picture board for the Grid Meter Cloud domain. Orange = domain event, blue = command, yellow = actor, purple = policy, pink = external system, green = read model.

```mermaid
flowchart LR
  classDef event fill:#ffb347,stroke:#c77700,color:#000
  classDef command fill:#6fa8dc,stroke:#1f5f99,color:#000
  classDef actor fill:#ffe599,stroke:#b8962e,color:#000
  classDef policy fill:#b4a7d6,stroke:#5e4b9a,color:#000
  classDef external fill:#f4a6c0,stroke:#b03a6a,color:#000
  classDef read fill:#93c47d,stroke:#38761d,color:#000

  op([Operator]):::actor --> c1[CreateSite]:::command --> e1{{SiteCreated}}:::event
  op --> c2[AddMeter]:::command --> e2{{MeterAdded}}:::event

  op --> c3[Request upload URL]:::command --> e3{{UploadTicketIssued}}:::event
  e3 --> x1[(S3 upload)]:::external --> e4{{MeterFileUploaded}}:::event
  e4 --> p1>On file uploaded: parse in Lambda]:::policy --> e5{{ReadingBatchesQueued}}:::event
  e5 --> p2>On batch received: ingest once, via inbox]:::policy --> c4[IngestReadingBatch]:::command --> e6{{ReadingsIngested}}:::event
  e6 --> r1[Consumption dashboard]:::read

  op --> r2[Bill estimate]:::read
  x2[(Wholesale price API)]:::external -. Polly retry + breaker .-> r2
  ff>Feature flag tariff-estimates]:::policy -. gates .-> r2

  op --> c5[IssueDemandResponse]:::command --> e7{{DemandResponseIssued}}:::event
  e7 --> p3>Outbox: store in same transaction, dispatch later]:::policy --> e8{{DemandResponsePublished}}:::event
  e8 --> p4>On published: notify customer]:::policy --> x3[(Notifier, log only)]:::external
```

## Aggregates and boundaries

| Aggregate | Commands | Events |
|---|---|---|
| `Site` (with `Meter`) | CreateSite, AddMeter | SiteCreated, MeterAdded |
| `DemandResponseEvent` | IssueDemandResponse | `DemandResponseIssued` (raised in the domain, persisted through the outbox) |
| Readings (ingestion) | IngestReadingBatch (idempotent through the inbox) | ReadingsIngested |

Every tenant-owned aggregate carries a `TenantId` that Finbuckle enforces with a global EF query filter.
