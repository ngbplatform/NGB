# NGB Auth Theme for Keycloak 26.5.6

This package is a filesystem theme intended to be mounted into `/opt/keycloak/themes/ngb-auth`.

## Included

- `login` theme based on `keycloak.v2`
  - branded login, password reset, email verification, required actions, OTP and other browser auth screens
  - NGB colors, typography, focus states and footer
- `email` theme based on `base`
  - branded HTML and text email wrapper for all standard email templates

## Deliberately not included

- `account` console reimplementation

Keycloak 26.x account console is React-based. For full production-grade account-console rebranding, the recommended route is a separate console build using `@keycloak/keycloak-account-ui`, not a superficial CSS hack.

## Install with Docker / Portainer

Mount the folder into the Keycloak container:

```yaml
volumes:
  - /path/to/ngb-auth-theme:/opt/keycloak/themes/ngb-auth:ro
```

Then restart Keycloak.

## Enable in the realm

Realm Settings -> Themes:

- Login Theme: `ngb-auth`
- Email Theme: `ngb-auth`

## Development tip

For fast iteration in development, temporarily disable theme caches:

```bash
--spi-theme-static-max-age=-1 --spi-theme-cache-themes=false --spi-theme-cache-templates=false
```

Do not keep those cache settings disabled in production.
