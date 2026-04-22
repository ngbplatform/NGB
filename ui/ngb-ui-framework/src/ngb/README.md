# NGB UI Framework

Structure:

- `primitives/` - Button/Input/Select/Badge
- `components/` - Header, Register, dialogs, layout shells
- `site/` - application shell building blocks, main menu store, and shared dashboard data/controller/presentation helpers
- `command-palette/` - command palette store, UI, search helpers, page context, and reusable static/heuristic item helpers
- `metadata/` - metadata store, list/register shells, typed catalog/document route page contracts, catalog/document route pages, list page data/reload controllers, filter drawer state/orchestration, entity form foundation, lookup hydration
- `accounting/` - chart-of-accounts, general-journal-entry, and period-closing API/types/navigation plus shared accounting pages/editors
- `lookup/` - shared lookup store/cache, default bootstrap config, lookup navigation, and lookup label prefetch helpers
- `editor/` - generic entity editor shell, headers, drawers, audit/effects/print pages, navigation, status, copy-draft, leave-guard, error contracts, persistence orchestration, entity behavior profiles, config-driven document actions, extension renderers/actions, imperative handle contracts, route-driven drawer orchestration, transient document drawer/copy-draft handlers, commit lifecycle handlers, document-effects helpers, and shared editor composables
- `reporting/` - report page shell, composer UI, sheet renderer, report API/types/navigation/session helpers, generic cell-action routing, default bootstrap config, and config-driven report adapters
- `auth/` - Keycloak adapter, auth store, route guard, and shared silent-check-sso hosting contract
- `api/` - authenticated HTTP client, API error normalization, transport contracts, and generic CRUD/audit/lookup clients
- `router/` - query helpers, route-query state composables, route param, share-link helpers, and generic route alias compatibility helpers
- `utils/` - generic client utilities reused across apps

Principle: NGB is a platform. Prefer structure + clarity over decoration.

Vite-hosted apps should add `ngbUiFrameworkPublicAssetsPlugin()` from `ngb-ui-framework/vite-public-assets` so framework-owned `/favicon.svg` and `/silent-check-sso.html` are published without app-local copies.

Boundary:

- `ngb-ui-framework` owns generic framework capabilities.
- industry apps should keep only domain pages, domain API clients, and industry workflows.
- if a module can be reused by another NGB app without knowing app-specific concepts, it belongs here.
