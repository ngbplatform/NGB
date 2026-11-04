---
name: ngb-application
description: Create or compose an independent NGB application using the official template, public SDK and application-owned modules.
---

Use this for bootstrap, adding module projects or changing host composition. For
record lifecycle or persistence implementation, load the corresponding reference.
Do not use it to modify platform internals to simulate a missing extension point.

1. Inspect `ai/kit.json`, `ngb.config.json`, solution/project files and the existing
   host composition. Confirm exact SDK/UI/CLI/Kit alignment with
   `node infrastructure/ngb.mjs doctor`.
2. Read [application composition](../../../ai/vendor/references/application.md). Add only projects
   needed by the feature, declare their roles in `ngb.config.json` and include them
   in the solution. Use public packages at `[$(NgbPlatformVersion)]`.
3. Register definitions, provider storage and runtime services in their proper
   layers. Keep controllers and `Program.cs` focused on composition. Preserve
   feature flags; installing development guidance never enables runtime features.
4. Run `node infrastructure/ngb.mjs validate`, then
   `node infrastructure/ngb.mjs validate --profile build` and the affected tests.

Done means the independent solution restores from declared package feeds, builds,
has explicit project roles and passes its applicable checks. Name any unavailable
integration prerequisite instead of claiming startup was verified.
