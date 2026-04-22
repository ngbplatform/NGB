---
title: Definitions source map
---

# Definitions source map

This page is the reading map for `NGB.Definitions`.

## Confirmed source anchors

```text
NGB.Definitions/NGB.Definitions.csproj
NGB.Runtime/NGB.Runtime.csproj
NGB.Runtime/Documents/DocumentService.cs
NGB.Runtime/Reporting/ReportEngine.cs
NGB.PropertyManagement.Api/Program.cs
```

## What the project boundary tells you first

`NGB.Definitions/NGB.Definitions.csproj` depends on:

- `NGB.Metadata`
- `NGB.Accounting`
- `NGB.OperationalRegisters`
- `NGB.ReferenceRegisters`
- `NGB.Persistence`
- `NGB.Core`
- `NGB.Tools`

That is the right place in the architecture for a **declarative registration layer**.

Definitions sit above descriptive metadata and core engines, but below Runtime orchestration.

## How Runtime proves the role of Definitions

`NGB.Runtime/Documents/DocumentService.cs` depends on a document type registry.

`NGB.Runtime/Reporting/ReportEngine.cs` depends on a report definition provider.

Those are the key clues.

Definitions are what Runtime reads to know:

- which document types exist;
- which reports exist;
- what their metadata is;
- which behaviors, policies, and runtime-visible artifacts should be registered.

## Why the API host is also an anchor here

`NGB.PropertyManagement.Api/Program.cs` calls:

```csharp
.AddPropertyManagementModule()
```

That is a composition-time proof that vertical modules bring their own definitions into the platform host.

In other words, Definitions are not only a static modeling concern. They are also a **composition concern**: a host chooses which vertical definitions become active by choosing which module registrations to add.

## What Definitions should contain conceptually

A healthy Definitions module is where you declare the platform-visible catalog, document, report, register, and workflow surface of a module.

That usually includes:

- document definitions;
- catalog definitions;
- report definitions;
- module-level registration of behaviors or policies;
- links to accounting/register semantics where relevant.

## What Definitions should not become

Avoid turning Definitions into:

- a second runtime layer;
- a provider-specific SQL layer;
- an ad hoc place for business service implementation.

Definitions should stay declarative and registration-oriented.

## Recommended reading order

```text
1. NGB.Definitions/NGB.Definitions.csproj
2. NGB.Runtime/Documents/DocumentService.cs
3. NGB.Runtime/Reporting/ReportEngine.cs
4. NGB.PropertyManagement.Api/Program.cs
```

After that, continue into the concrete vertical module registration files in your local checkout.

## Safe change rules for Definitions

### Keep definitions explicit

If the platform exposes a new catalog, document, or report, register it through Definitions rather than through scattered runtime conditionals.

### Keep definitions declarative

Runtime should execute. Definitions should describe and register.

### Keep shared Definitions free of vertical leakage

Shared platform definitions should remain general-purpose. Vertical definitions belong in vertical modules.
