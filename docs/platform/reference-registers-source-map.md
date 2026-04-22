---
title: Reference Registers source map
---

# Reference Registers source map

This page is the architectural reading map for `NGB.ReferenceRegisters`.

## Confirmed source anchors

```text
NGB.ReferenceRegisters/NGB.ReferenceRegisters.csproj
NGB.Runtime/NGB.Runtime.csproj
NGB.Runtime/Documents/DocumentService.cs
NGB.PropertyManagement.Api/Program.cs
```

## What the project boundary tells you first

`NGB.ReferenceRegisters/NGB.ReferenceRegisters.csproj` depends on:

- `NGB.Tools`
- `NGB.Core`
- `NGB.Metadata`

That is a strong hint about its role.

Reference Registers are not only a raw engine. They also depend on metadata because they model **reference-style effective state** that usually needs descriptive register shape information.

## How Runtime proves the role of Reference Registers

`NGB.Runtime/NGB.Runtime.csproj` depends on `NGB.ReferenceRegisters`, so Runtime is responsible for coordinating RR behavior.

`NGB.Runtime/Documents/DocumentService.cs` exposes reference-register effects as part of the document effects surface, which is exactly what users need for explainability.

## Why the metadata dependency matters

Unlike a purely minimal engine, RR already shows a tighter link to metadata at the project boundary. That fits the platform idea that reference registers often represent controlled effective business facts such as prices, policies, or other latest-effective reference-like values.

## What to inspect next in your local checkout

After reading this boundary map, continue into the RR engine files that define:

- write semantics;
- effective-state resolution;
- metadata/descriptor contracts for RR records;
- the coupling points used by runtime or provider code.

## Safe change rules for RR

### Keep write semantics explicit

Reference Registers are valuable because the effective state remains explainable through writes rather than through silent overwrites.

### Keep infrastructure out of the engine

Provider-specific persistence and SQL stay outside `NGB.ReferenceRegisters`.

### Use Runtime and Definitions to express business meaning

A price register, policy register, or any other reference-style register should be expressed through definitions/runtime composition instead of pushing vertical business meaning into the shared RR core.
