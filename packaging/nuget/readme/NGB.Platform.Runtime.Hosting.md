# NGB.Platform.Runtime.Hosting

Generic-host lifecycle adapters for NGB Platform Runtime.

## Install

```bash
dotnet add package NGB.Platform.Runtime.Hosting
```

## What It Contains

- Explicit opt-in startup validation for composed NGB definitions.
- Generic-host lifecycle integration without coupling the Runtime application layer to `IHostedService`.

## Usage

```csharp
services
    .AddNgbRuntime()
    .AddNgbRuntimeStartupValidation();
```

Application hosts should enable this integration so invalid metadata and service bindings fail before traffic or background work is accepted.
