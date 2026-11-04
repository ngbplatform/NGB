# Authorization

- An authenticated, active user with the application's Administrator role has full application permissions, including Attachments & Notes and newly registered capabilities. Do not require manual permission grants for administrators.
- Support both platform users linked to Keycloak and Keycloak administrators without a pre-existing platform user record. The trusted Keycloak administrator role is `ngb-admin`; application administrator roles are identified by their explicitly registered codes, never by editable display names.
- Keep authorization consistent between the API and UI in Agency Billing, Property Management, Trade and CRM. Deployment feature flags remain independent of permissions, and inactive accounts remain blocked.

# Code style

- Follow nearby established code, repository formatter settings and the user's formatting conventions. Preserve readable indentation, blank lines and method structure; do not compress unrelated statements onto one line.
- Review the final diff for style consistency and keep formatting changes scoped to the work.

# MCP and AI Assistant architecture

- MCP Server is a thin adapter over existing application services, business logic and authorization. Keep its generic implementation independent of Property Management and other verticals.
- Work on MCP or AI Assistant does not authorize changes to business logic, SQL readers, database schemas or migrations, report definitions or calculations, domain filters, business contracts, or application permission rules. Do not add business capabilities to make an assistant scenario pass.
- Expose and correctly invoke existing authorized capabilities through the adapter. If a scenario cannot be supported by the existing application services, report the limitation; changing the business layer requires a separate explicit user request.
