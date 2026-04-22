---
title: Core and tools
description: Verified and inferred boundaries for NGB.Core and NGB.Tools, the lowest reusable layers under the platform runtime and infrastructure stack.
---

# Core and tools

<div class="doc-badge-row">
  <span class="doc-badge doc-badge--verified">Verified project boundaries</span>
  <span class="doc-badge doc-badge--inferred">Layering interpretation</span>
</div>

## Verified anchors

```text
NGB.Core/NGB.Core.csproj
NGB.Tools/NGB.Tools.csproj
NGB.Core/Documents/DocumentStatus.cs
NGB.Core/Documents/DocumentRecord.cs
NGB.Core/AuditLog/AuditEvent.cs
NGB.Core/Dimensions/DimensionBag.cs
NGB.Tools/Exceptions/NgbConfigurationViolationException.cs
NGB.Tools/Exceptions/NgbValidationException.cs
NGB.Tools/Normalization/CodeNormalizer.cs
NGB.Tools/Extensions/DeterministicGuid.cs
```

## What is directly visible

The verified project files show that:

- `NGB.Tools` is the lowest-level reusable library in this slice and has no project reference back into `NGB.Core`;
- `NGB.Core` depends on `NGB.Tools`;
- both projects are non-publishable reusable class libraries.

The verified file inventory also shows the kinds of concerns that actually live in each project.

## What belongs in `NGB.Tools`

`NGB.Tools` contains generic platform utilities that should remain reusable across domains and verticals:

- exception base types and error taxonomy;
- normalization helpers such as code and identifier normalization;
- low-level extensions for dates, GUIDs, enums, JSON, and time providers;
- deterministic helper utilities.

These are foundational mechanics. They should not know about documents, reporting, catalogs, or provider-specific persistence.

## What belongs in `NGB.Core`

`NGB.Core` contains durable platform-level domain primitives:

- document records and document status;
- document-relationship records and deterministic relationship ids;
- audit-log models such as `AuditEvent` and `AuditFieldChange`;
- catalog records and catalog-specific exceptions;
- dimension models such as `DimensionBag` and `DimensionScope`;
- reporting-specific exception types.

This is still lower-level than runtime orchestration. The project holds durable concepts, not the host or workflow code that executes them.

## Boundary rule

Use this split as the litmus test:

- if the code is a cross-cutting primitive or utility with no business orchestration, it likely belongs in `NGB.Tools`;
- if the code is a stable business-platform concept shared across modules, it likely belongs in `NGB.Core`;
- if the code coordinates workflows, persistence, hosting, or provider execution, it belongs above these projects.

## What should stay out

Keep the following out of both projects:

- SQL generation and persistence-provider details;
- host startup and dependency-injection composition;
- vertical-specific policies and workflows;
- runtime orchestration that coordinates multiple collaborators.

## Related pages

- [Layering and Dependencies](/architecture/layering-and-dependencies)
- [Layering rules](/reference/layering-rules)
- [Platform projects](/reference/platform-projects)
