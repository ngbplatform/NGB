---
title: "Runtime Execution Core Dense Source Map"
description: "Dense source-anchored chapter for the runtime execution core across document orchestration, reporting orchestration, persistence boundaries, and host composition."
---

# Runtime Execution Core Dense Source Map

<div class="doc-badge-row">
  <span class="doc-badge verified">Verified</span>
  <span class="doc-badge inferred">Inferred synthesis</span>
</div>

## How to read this page

This chapter is intentionally denser than the overview pages.

It is meant to answer:

- where the platform execution core actually lives;
- how document orchestration and reporting orchestration coexist inside Runtime;
- where persistence boundaries are explicit;
- where PostgreSQL execution begins;
- how a vertical host composes the whole stack.

## 1. The execution core is a composition of contracts, Runtime, persistence abstractions, and provider execution

The verified file set shows that NGB’s execution core is not a single class and not a single project.

A grounded reading of the source shows these layers:

1. **application contracts** define the callable business surface;
2. **Runtime classes** orchestrate document and reporting behavior;
3. **persistence abstractions** isolate generic document storage access;
4. **PostgreSQL reporting classes** perform provider-specific dataset lookup, SQL generation, and row execution;
5. **vertical hosts** compose those layers into a running application.

This matters because it explains why NGB can remain both metadata-driven and modular without pushing every concern into either the API host or the PostgreSQL provider.

## 2. The document-facing application surface is already rich at the abstraction boundary

**Verified anchor**

- `NGB.Application.Abstractions/Services/IDocumentService.cs`

The verified `IDocumentService` contract already includes:

- metadata access;
- page and point lookup;
- cross-type lookup helpers;
- draft lifecycle operations;
- posting lifecycle operations;
- deletion-mark lifecycle operations;
- relationship graph retrieval;
- effects retrieval;
- derivation discovery and execution.

### Why this matters

This proves the execution core is designed around a high-level business surface, not around leaking low-level repository calls upward.

It also proves that navigation/explainability features such as **Document Flow** and **Effects** are part of the application boundary, not just internal helpers.

## 3. `DocumentService` is the document-side execution hub

**Verified anchor**

- `NGB.Runtime/Documents/DocumentService.cs`

The verified constructor and method set show that `DocumentService` is where multiple document concerns converge:

- metadata-driven type resolution;
- draft CRUD;
- document part parsing and validation;
- draft payload validation;
- UoW transaction coordination;
- posting lifecycle delegation;
- derivation action discovery and execution;
- relationship graph assembly;
- effects retrieval;
- UI effects composition;
- optional audit writing.

### What this proves

`DocumentService` is not just “document CRUD”. It is a runtime orchestration center for the mutable document surface of the platform.

### Boundary insight

The class does not itself encode all physical persistence details. Instead, it coordinates a set of persistence abstractions and runtime collaborators. That separation is the key design signal of the execution core.

## 4. The document persistence boundary is explicit and layered

**Verified anchors**

- `NGB.Persistence/Documents/IDocumentRepository.cs`
- `NGB.Persistence/Documents/Universal/IDocumentReader.cs`
- `NGB.Persistence/Documents/Universal/IDocumentWriter.cs`
- `NGB.Persistence/Documents/Universal/IDocumentPartsReader.cs`
- `NGB.Persistence/Documents/Universal/IDocumentPartsWriter.cs`

### 4.1. Registry versus typed payload split

`IDocumentRepository` explicitly documents that:

- it owns the `documents` registry row;
- typed data belongs in `doc_{type_code}` and `doc_{type_code}__{part}` tables;
- state transitions should use `GetForUpdateAsync` inside an active transaction.

That is a strong, direct signal that the platform distinguishes between:

- common lifecycle/header state; and
- typed document payload.

### 4.2. Universal read/write shape

The universal interfaces show another deliberate split:

- `IDocumentReader` handles head-table reads, paging, cross-type lookup, and multi-type graph support loads;
- `IDocumentWriter` handles head-table upsert;
- `IDocumentPartsReader` reads raw part rows keyed by physical table name;
- `IDocumentPartsWriter` applies replace-by-document semantics for draft part editing.

### Why this matters

The execution core therefore uses two distinct mutation models at once:

- **mutable draft editing** for head and part tables;
- **state-transition orchestration** for registry status and posting lifecycle.

That split is essential for understanding how NGB can support rich draft editing without giving up controlled posting and lifecycle transitions.

## 5. Derivation is part of the execution core, not a UI-only feature

**Verified anchor**

- `NGB.Runtime/Documents/Derivations/IDocumentDerivationService.cs`

The verified interface comments explicitly show that derivation:

- is driven by `DocumentDerivationDefinition` from `DefinitionsRegistry`;
- writes relationship codes such as `created_from` and `based_on`;
- creates a Draft only and does not post it.

### Why this matters

This proves that document derivation belongs to the same runtime orchestration core as draft CRUD and posting lifecycle, rather than being a front-end convenience layer.

It also confirms that relationship semantics are not incidental—they are part of the platform’s document model.

## 6. Reporting orchestration sits beside document orchestration in Runtime

**Verified anchors**

- `NGB.Runtime/Reporting/ReportEngine.cs`
- `NGB.Runtime/Reporting/ReportExecutionPlanner.cs`
- `NGB.Runtime/Reporting/ReportSheetBuilder.cs`

The verified reporting files show a second orchestration pipeline inside Runtime:

- `ReportEngine` coordinates execution;
- `ReportExecutionPlanner` normalizes request/layout into a plan;
- `ReportSheetBuilder` produces UI-facing sheets from plan + data page.

### Why this matters

This proves that Runtime is not only a document runtime. It is the platform’s **execution orchestration layer** for both:

- mutable business document workflows; and
- analytical/reporting workflows.

These two pipelines are different in nature, but they live in the same orchestration tier.

## 7. PostgreSQL begins at the provider execution boundary, not earlier

**Verified anchors**

- `NGB.PostgreSql/Reporting/IPostgresReportDatasetSource.cs`
- `NGB.PostgreSql/Reporting/PostgresReportDatasetCatalog.cs`
- `NGB.PostgreSql/Reporting/PostgresReportSqlBuilder.cs`
- `NGB.PostgreSql/Reporting/PostgresReportDatasetExecutor.cs`

The verified PostgreSQL reporting files show a provider-side internal pipeline:

1. dataset registration (`IPostgresReportDatasetSource`);
2. dataset lookup (`PostgresReportDatasetCatalog`);
3. SQL translation (`PostgresReportSqlBuilder`);
4. execution + row materialization (`PostgresReportDatasetExecutor`).

### Why this matters

This proves PostgreSQL execution does **not** begin in Runtime planning. Runtime stops at plan/sheet orchestration; provider-specific SQL rendering and execution happen later, inside `NGB.PostgreSql`.

That separation is one of the most important architectural boundaries in the execution core.

## 8. Project references confirm the module split is real

**Verified anchors**

- `NGB.Runtime/NGB.Runtime.csproj`
- `NGB.PostgreSql/NGB.PostgreSql.csproj`

### 8.1. What `NGB.Runtime.csproj` proves

The verified project references show Runtime depends on:

- Accounting;
- Application.Abstractions;
- Contracts;
- Definitions;
- OperationalRegisters;
- ReferenceRegisters;
- Persistence;
- Metadata.

This is strong evidence that Runtime is designed as the platform orchestration layer that sits above domain engines and persistence contracts.

### 8.2. What `NGB.PostgreSql.csproj` proves

The verified project references show the PostgreSQL module depends on:

- Core;
- Accounting;
- Metadata;
- OperationalRegisters;
- ReferenceRegisters;
- Persistence;
- Application.Abstractions;
- Tools.

This is strong evidence that the PostgreSQL provider is not just a document repository assembly. It is a broader infrastructure/provider module that serves multiple platform subsystems.

## 9. Host composition proves the execution core is assembled, not duplicated

**Verified anchor**

- `NGB.PropertyManagement.Api/Program.cs`

The verified API host composes:

- `.AddNgbRuntime()`
- `.AddNgbPostgres(cs)`
- vertical module/runtime/postgres registrations

### Why this matters

This proves the vertical host is mainly a composition root. It does not own the execution logic of documents or reports directly. Instead, it wires together:

- shared platform Runtime;
- shared platform PostgreSQL provider;
- vertical extensions.

That is the clearest verified proof that the runtime execution core is reusable platform infrastructure rather than per-vertical host code.

## 10. What the dense source map proves about the platform execution core

Taken together, the verified anchors support a clear reading of NGB’s execution core:

1. **business-facing contracts** expose a high-level surface (`IDocumentService`);
2. **Runtime** orchestrates document and reporting flows;
3. **persistence abstractions** isolate generic document-table access from orchestration;
4. **PostgreSQL provider classes** handle provider-specific dataset and SQL execution;
5. **vertical hosts** assemble the platform modules and vertical modules into one runtime application.

That is the execution core that the verified anchors can actually prove.

## 11. Continue with

To keep reading this topic in more detail, continue with:

- [Runtime Execution Core Collaborators Map](/platform/runtime-execution-core-collaborators-map)
- [Document Subsystem Dense Source Map](/platform/document-subsystem-dense-source-map)
- [Reporting Subsystem Dense Source Map](/platform/reporting-subsystem-dense-source-map)
- [Ops and Tooling Subsystem Dense Source Map](/platform/ops-tooling-dense-source-map)
