---
title: Security and Permissions
---

# Security and Permissions

NGB uses Keycloak as the identity provider and keeps application authorization metadata in the NGB database.

Keycloak owns authentication, SSO sessions, external user identity, and enabled/disabled identity-provider users. NGB owns application roles, permission assignments, effective access snapshots, access-version invalidation, menu filtering, metadata capabilities, report access, and security audit records.

## Core Model

The authorization model is deny-by-default.

An authenticated Keycloak user is projected into `platform_users`. Application access is resolved from:

- active `platform_users` row;
- active `platform_roles`;
- `platform_role_permissions`;
- `platform_user_roles`;
- `platform_user_access_versions`.

Permission keys have three parts:

```text
resource_kind.resource_code.action_code
```

The resource code may contain dots because NGB document, catalog, report, and page codes are namespaced, for example:

```text
document.pm.lease.view
report.pm.occupancy.summary.execute
system.users.manage
```

## Enforcement Boundary

Backend enforcement is the source of truth.

The UI may hide menu items, disable actions, and show access-denied states, but every sensitive operation is checked by the backend:

- main menu contributors are filtered by permission;
- document and catalog metadata are filtered and returned with permission-aware capabilities;
- document and catalog list/read/create/update/post/delete-style actions are checked by runtime wrappers;
- report definitions, execution, export, variants, and cell actions are checked by report controllers;
- command palette results are filtered server-side;
- user and role management APIs require system permissions.

## Users

Users are created and updated through the NGB backend. The backend calls Keycloak Admin REST API and then updates `platform_users`.

The normal UI does not hard-delete users. Production user lifecycle is:

- create;
- update;
- deactivate;
- reactivate.

This preserves audit history, report variants, ownership links, and foreign-key integrity.

## Roles

Roles and permissions are stored in NGB tables. Property Management seeds default application roles such as administrator, accountant, AR/AP clerk, property manager, maintenance coordinator, auditor, and read-only user.

Seeders are additive: existing roles are not overwritten.

## Bootstrap Admin

The Keycloak role `ngb-admin` is treated as a bootstrap application administrator. It can access all permissions while the first NGB application roles are created or repaired.

For regular users, Keycloak roles are not the source of application authorization. Use NGB roles and permissions.

## Configuration

The API registers the Keycloak Admin client only when `KeycloakAdminClientSettings` is configured. Typical settings are:

```text
KeycloakAdminClientSettings:BaseUrl
KeycloakAdminClientSettings:Realm
KeycloakAdminClientSettings:ClientId
KeycloakAdminClientSettings:ClientSecret
```

Do not expose Keycloak Admin REST credentials to the frontend.

## Local Testing

For Property Management, run migrations and seed defaults before testing security UI:

```bash
dotnet run --project NGB.PropertyManagement.Migrator -- seed-defaults --connection "$NGB_PM_CONNECTION"
```

Then open:

```text
/admin/security/users
/admin/security/roles
```

The e2e suite has mocked UI coverage for the security routes. Full end-to-end user provisioning still requires a working Keycloak test environment because the backend intentionally owns Keycloak Admin REST calls.

