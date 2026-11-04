# Identity, permissions and feature flags

Target: NGB 3.4.0. Public contracts:
[Runtime](contracts/NGB.Platform.Runtime.json),
[ASP.NET hosting](contracts/NGB.Platform.Hosting.AspNetCore.json),
[application services](contracts/NGB.Platform.Application.Abstractions.json).

Keycloak settings establish issuer, metadata endpoint and allowed client IDs.
Production metadata uses HTTPS; loopback HTTP is limited to disposable tests.
Keep secrets in the configured secret store/environment, never in instructions,
versioned settings, prompts or evidence. Use the starter's authentication wiring.

`INgbPermissionDefinitionSource` contributes permission definitions.
`INgbAccessChecker.RequireAsync(resourceKind, resourceCode, actionCode,
CancellationToken)` enforces a capability; `HasAsync` supports availability
queries. Record-level authorization and feature prerequisites still apply.
Reuse the public authorized application services instead of exposing raw readers.

Configure explicit application administrator role codes through
`NgbAdministratorOptions.ApplicationRoleCodes`. Active administrators receive all
registered capabilities without manual grants. Trusted Keycloak `ngb-admin` also
works before a platform user exists. Editable role names are never authority.
Inactive accounts stay blocked. Optional feature flags remain independent of
permissions. Personal state may require a linked platform user: the starter tests
verify that unlinked administrators can manage roles while personal Work Center
state retains its existing linked-user prerequisite.

Test anonymous, insufficient permission, active application administrator, trusted
Keycloak administrator without a platform record, inactive identity and disabled
feature separately. For each UI action, assert the corresponding backend result.
Changing a permission display name must not change access. Newly contributed
permissions must be accessible to existing administrators without adding grants.

## User and role administration

Expose the existing `SecurityControllerBase` from an authorized application
controller. It checks `NgbSystemPermissions` before invoking trusted
`IUserAccessManagementService` and `IRoleManagementService` operations. Those
services do not replace the caller's authorization boundary.

User creation links or creates an identity-provider user, then persists the platform
record, assignments and provisioning operation. Updates retain that identity;
deactivation/reactivation update both platform and identity-provider state.
Configure `KeycloakAdminClientSettings` separately from token validation: base URL,
realm and a confidential client with only the required realm user-management roles.
The starter realm permits username editing because the existing admin client uses
email as username when updating a profile. Keep the client secret outside source.

Role and assignment changes update access versions and audit evidence. Read current
access through `/api/security/me/access`; administrator reads of another user's
effective access retain the defined resource groups, with empty `Actions` for
resources the user cannot access. An empty permission set does not imply an empty
group list. User-list cursors bind the active-status filter and use bounded pages.

Registered administrator roles remain protected: applications may rename their
display labels, but cannot replace their reserved code, deactivate the role or
replace its implicit full-access permissions with explicit grants. Ordinary roles
support creation, updates, permission replacement and activation through the public
API. Preserve the distinction between role state and the user's active state.

`SecurityAdministrationHttpTests` in the independent example exercises these routes
against disposable PostgreSQL and Keycloak instances. It covers both administrator
kinds, user/profile/role lifecycle, access-version changes, audit records, cursor
filter binding and refusal for ungranted, inactive or anonymous callers.

## Identity and administration data

`ActorIdentity` comes from the trusted execution host. Its external subject and
roles describe an authenticated context; constructing the record does not
authenticate a request. `DisplayName` is presentation data. Continue to resolve
registered application administrator role codes and the trusted `ngb-admin` role
through the standard services, retaining active-account checks.

`IdentityProviderUserDto` and `IdentityProviderUserBatch` are administration
projections, not permission snapshots. Create/update requests belong behind the
authorized user-management workflow; keep temporary passwords out of diagnostics,
audit metadata and Kit files. A page snapshot reader must use its in-process
projection/cache and must not issue one remote identity-provider call per row.

Deployment `NgbFeatureDefinition` metadata stays independent of these identities
and permissions. An administrator grant does not enable a disabled feature.
