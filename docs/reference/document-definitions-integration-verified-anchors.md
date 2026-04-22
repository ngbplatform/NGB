---
title: "Document + Definitions Integration Verified Anchors"
description: "Compact evidence index for the document + definitions integration chapter."
---

# Document + Definitions Integration Verified Anchors

This page lists the source files that were directly verified for the document + definitions integration chapter.

## Definitions

- `NGB.Definitions/DefinitionsRegistry.cs`
- `NGB.Definitions/Documents/Relationships/DocumentRelationshipTypeDefinition.cs`
- `NGB.Definitions/Documents/Derivations/DocumentDerivationDefinition.cs`

## Metadata

- `NGB.Metadata/Documents/Hybrid/DocumentTableMetadata.cs`
- `NGB.Metadata/Documents/Hybrid/DocumentColumnMetadata.cs`

## Runtime contracts and services

- `NGB.Application.Abstractions/Services/IDocumentService.cs`
- `NGB.Runtime/Documents/DocumentService.cs`
- `NGB.Runtime/Documents/Derivations/IDocumentDerivationService.cs`

## Persistence boundary

- `NGB.Persistence/Documents/IDocumentRepository.cs`
- `NGB.Persistence/Documents/Universal/IDocumentReader.cs`
- `NGB.Persistence/Documents/Universal/IDocumentWriter.cs`
- `NGB.Persistence/Documents/Universal/IDocumentPartsReader.cs`
- `NGB.Persistence/Documents/Universal/IDocumentPartsWriter.cs`

## Notes

This evidence index is intentionally narrow. It proves the integration boundary discussed in:

- [Document + Definitions Integration Dense Source Map](/platform/document-definitions-integration-dense-source-map)
- [Document + Definitions Integration Collaborators Map](/platform/document-definitions-integration-collaborators-map)

It does **not** claim complete coverage of all posting handlers, graph readers, or effect readers for the document subsystem.
