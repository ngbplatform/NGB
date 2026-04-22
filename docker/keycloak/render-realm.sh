#!/usr/bin/env bash
set -euo pipefail

output_file="${1:-/opt/keycloak/data/import/ngb-realm.json}"
mkdir -p "$(dirname "$output_file")"

json_escape() {
  local value="${1-}"
  value="${value//\\/\\\\}"
  value="${value//\"/\\\"}"
  value="${value//$'\n'/\\n}"
  value="${value//$'\r'/\\r}"
  value="${value//$'\t'/\\t}"
  printf '%s' "$value"
}

cat >"$output_file" <<EOF
{
  "realm": "$(json_escape "${KEYCLOAK_REALM}")",
  "enabled": true,
  "displayName": "$(json_escape "${KEYCLOAK_REALM_DISPLAY_NAME}")",
  "displayNameHtml": "$(json_escape "${KEYCLOAK_REALM_DISPLAY_NAME_HTML}")",
  "sslRequired": "$(json_escape "${KEYCLOAK_SSL_REQUIRED}")",
  "accessTokenLifespan": ${KEYCLOAK_ACCESS_TOKEN_LIFESPAN_SECONDS},
  "loginTheme": "$(json_escape "${KEYCLOAK_THEME_NAME}")",
  "emailTheme": "$(json_escape "${KEYCLOAK_THEME_NAME}")",
  "loginWithEmailAllowed": ${KEYCLOAK_LOGIN_WITH_EMAIL_ALLOWED},
  "registrationAllowed": ${KEYCLOAK_REGISTRATION_ALLOWED},
  "registrationEmailAsUsername": ${KEYCLOAK_REGISTRATION_EMAIL_AS_USERNAME},
  "resetPasswordAllowed": ${KEYCLOAK_RESET_PASSWORD_ALLOWED},
  "verifyEmail": ${KEYCLOAK_VERIFY_EMAIL},
  "rememberMe": ${KEYCLOAK_REMEMBER_ME},
  "bruteForceProtected": ${KEYCLOAK_BRUTE_FORCE_PROTECTED},
  "roles": {
    "realm": [
      {
        "name": "ngb-admin",
        "description": "Administrative role for NGB vertical applications."
      },
      {
        "name": "ngb-user",
        "description": "Standard end-user role for NGB vertical applications."
      }
    ]
  },
  "defaultDefaultClientScopes": [
    "web-origins",
    "acr",
    "roles",
    "profile",
    "basic",
    "email"
  ],
  "defaultOptionalClientScopes": [
    "offline_access"
  ],
  "clientScopes": [
    {
      "name": "web-origins",
      "description": "OpenID Connect scope for add allowed web origins to the access token",
      "protocol": "openid-connect",
      "attributes": {
        "include.in.token.scope": "false",
        "consent.screen.text": "",
        "display.on.consent.screen": "false"
      },
      "protocolMappers": [
        {
          "name": "allowed web origins",
          "protocol": "openid-connect",
          "protocolMapper": "oidc-allowed-origins-mapper",
          "consentRequired": false,
          "config": {
            "introspection.token.claim": "true",
            "access.token.claim": "true"
          }
        }
      ]
    },
    {
      "name": "basic",
      "description": "OpenID Connect scope for add all basic claims to the token",
      "protocol": "openid-connect",
      "attributes": {
        "include.in.token.scope": "false",
        "display.on.consent.screen": "false"
      },
      "protocolMappers": [
        {
          "name": "auth_time",
          "protocol": "openid-connect",
          "protocolMapper": "oidc-usersessionmodel-note-mapper",
          "consentRequired": false,
          "config": {
            "user.session.note": "AUTH_TIME",
            "id.token.claim": "true",
            "introspection.token.claim": "true",
            "access.token.claim": "true",
            "claim.name": "auth_time",
            "jsonType.label": "long"
          }
        },
        {
          "name": "sub",
          "protocol": "openid-connect",
          "protocolMapper": "oidc-sub-mapper",
          "consentRequired": false,
          "config": {
            "introspection.token.claim": "true",
            "access.token.claim": "true"
          }
        }
      ]
    },
    {
      "name": "acr",
      "description": "OpenID Connect scope for add acr (authentication context class reference) to the token",
      "protocol": "openid-connect",
      "attributes": {
        "include.in.token.scope": "false",
        "display.on.consent.screen": "false"
      },
      "protocolMappers": [
        {
          "name": "acr loa level",
          "protocol": "openid-connect",
          "protocolMapper": "oidc-acr-mapper",
          "consentRequired": false,
          "config": {
            "id.token.claim": "true",
            "introspection.token.claim": "true",
            "access.token.claim": "true"
          }
        }
      ]
    },
    {
      "name": "profile",
      "description": "OpenID Connect built-in scope: profile",
      "protocol": "openid-connect",
      "attributes": {
        "include.in.token.scope": "true",
        "consent.screen.text": "\${profileScopeConsentText}",
        "display.on.consent.screen": "true"
      },
      "protocolMappers": [
        {
          "name": "website",
          "protocol": "openid-connect",
          "protocolMapper": "oidc-usermodel-attribute-mapper",
          "consentRequired": false,
          "config": {
            "introspection.token.claim": "true",
            "userinfo.token.claim": "true",
            "user.attribute": "website",
            "id.token.claim": "true",
            "access.token.claim": "true",
            "claim.name": "website",
            "jsonType.label": "String"
          }
        },
        {
          "name": "nickname",
          "protocol": "openid-connect",
          "protocolMapper": "oidc-usermodel-attribute-mapper",
          "consentRequired": false,
          "config": {
            "introspection.token.claim": "true",
            "userinfo.token.claim": "true",
            "user.attribute": "nickname",
            "id.token.claim": "true",
            "access.token.claim": "true",
            "claim.name": "nickname",
            "jsonType.label": "String"
          }
        },
        {
          "name": "updated at",
          "protocol": "openid-connect",
          "protocolMapper": "oidc-usermodel-attribute-mapper",
          "consentRequired": false,
          "config": {
            "introspection.token.claim": "true",
            "userinfo.token.claim": "true",
            "user.attribute": "updatedAt",
            "id.token.claim": "true",
            "access.token.claim": "true",
            "claim.name": "updated_at",
            "jsonType.label": "long"
          }
        },
        {
          "name": "given name",
          "protocol": "openid-connect",
          "protocolMapper": "oidc-usermodel-attribute-mapper",
          "consentRequired": false,
          "config": {
            "introspection.token.claim": "true",
            "userinfo.token.claim": "true",
            "user.attribute": "firstName",
            "id.token.claim": "true",
            "access.token.claim": "true",
            "claim.name": "given_name",
            "jsonType.label": "String"
          }
        },
        {
          "name": "picture",
          "protocol": "openid-connect",
          "protocolMapper": "oidc-usermodel-attribute-mapper",
          "consentRequired": false,
          "config": {
            "introspection.token.claim": "true",
            "userinfo.token.claim": "true",
            "user.attribute": "picture",
            "id.token.claim": "true",
            "access.token.claim": "true",
            "claim.name": "picture",
            "jsonType.label": "String"
          }
        },
        {
          "name": "birthdate",
          "protocol": "openid-connect",
          "protocolMapper": "oidc-usermodel-attribute-mapper",
          "consentRequired": false,
          "config": {
            "introspection.token.claim": "true",
            "userinfo.token.claim": "true",
            "user.attribute": "birthdate",
            "id.token.claim": "true",
            "access.token.claim": "true",
            "claim.name": "birthdate",
            "jsonType.label": "String"
          }
        },
        {
          "name": "locale",
          "protocol": "openid-connect",
          "protocolMapper": "oidc-usermodel-attribute-mapper",
          "consentRequired": false,
          "config": {
            "introspection.token.claim": "true",
            "userinfo.token.claim": "true",
            "user.attribute": "locale",
            "id.token.claim": "true",
            "access.token.claim": "true",
            "claim.name": "locale",
            "jsonType.label": "String"
          }
        },
        {
          "name": "profile",
          "protocol": "openid-connect",
          "protocolMapper": "oidc-usermodel-attribute-mapper",
          "consentRequired": false,
          "config": {
            "introspection.token.claim": "true",
            "userinfo.token.claim": "true",
            "user.attribute": "profile",
            "id.token.claim": "true",
            "access.token.claim": "true",
            "claim.name": "profile",
            "jsonType.label": "String"
          }
        },
        {
          "name": "family name",
          "protocol": "openid-connect",
          "protocolMapper": "oidc-usermodel-attribute-mapper",
          "consentRequired": false,
          "config": {
            "introspection.token.claim": "true",
            "userinfo.token.claim": "true",
            "user.attribute": "lastName",
            "id.token.claim": "true",
            "access.token.claim": "true",
            "claim.name": "family_name",
            "jsonType.label": "String"
          }
        },
        {
          "name": "username",
          "protocol": "openid-connect",
          "protocolMapper": "oidc-usermodel-attribute-mapper",
          "consentRequired": false,
          "config": {
            "introspection.token.claim": "true",
            "userinfo.token.claim": "true",
            "user.attribute": "username",
            "id.token.claim": "true",
            "access.token.claim": "true",
            "claim.name": "preferred_username",
            "jsonType.label": "String"
          }
        },
        {
          "name": "full name",
          "protocol": "openid-connect",
          "protocolMapper": "oidc-full-name-mapper",
          "consentRequired": false,
          "config": {
            "id.token.claim": "true",
            "introspection.token.claim": "true",
            "access.token.claim": "true",
            "userinfo.token.claim": "true"
          }
        },
        {
          "name": "middle name",
          "protocol": "openid-connect",
          "protocolMapper": "oidc-usermodel-attribute-mapper",
          "consentRequired": false,
          "config": {
            "introspection.token.claim": "true",
            "userinfo.token.claim": "true",
            "user.attribute": "middleName",
            "id.token.claim": "true",
            "access.token.claim": "true",
            "claim.name": "middle_name",
            "jsonType.label": "String"
          }
        },
        {
          "name": "gender",
          "protocol": "openid-connect",
          "protocolMapper": "oidc-usermodel-attribute-mapper",
          "consentRequired": false,
          "config": {
            "introspection.token.claim": "true",
            "userinfo.token.claim": "true",
            "user.attribute": "gender",
            "id.token.claim": "true",
            "access.token.claim": "true",
            "claim.name": "gender",
            "jsonType.label": "String"
          }
        },
        {
          "name": "zoneinfo",
          "protocol": "openid-connect",
          "protocolMapper": "oidc-usermodel-attribute-mapper",
          "consentRequired": false,
          "config": {
            "introspection.token.claim": "true",
            "userinfo.token.claim": "true",
            "user.attribute": "zoneinfo",
            "id.token.claim": "true",
            "access.token.claim": "true",
            "claim.name": "zoneinfo",
            "jsonType.label": "String"
          }
        }
      ]
    },
    {
      "name": "roles",
      "description": "OpenID Connect scope for add user roles to the access token",
      "protocol": "openid-connect",
      "attributes": {
        "include.in.token.scope": "false",
        "consent.screen.text": "\${rolesScopeConsentText}",
        "display.on.consent.screen": "true"
      },
      "protocolMappers": [
        {
          "name": "realm roles",
          "protocol": "openid-connect",
          "protocolMapper": "oidc-usermodel-realm-role-mapper",
          "consentRequired": false,
          "config": {
            "user.attribute": "foo",
            "introspection.token.claim": "true",
            "access.token.claim": "true",
            "id.token.claim": "true",
            "userinfo.token.claim": "true",
            "claim.name": "realm_access.roles",
            "jsonType.label": "String",
            "multivalued": "true"
          }
        },
        {
          "name": "root realm roles",
          "protocol": "openid-connect",
          "protocolMapper": "oidc-usermodel-realm-role-mapper",
          "consentRequired": false,
          "config": {
            "user.attribute": "foo",
            "introspection.token.claim": "true",
            "access.token.claim": "true",
            "id.token.claim": "true",
            "userinfo.token.claim": "true",
            "claim.name": "roles",
            "jsonType.label": "String",
            "multivalued": "true"
          }
        },
        {
          "name": "audience resolve",
          "protocol": "openid-connect",
          "protocolMapper": "oidc-audience-resolve-mapper",
          "consentRequired": false,
          "config": {
            "introspection.token.claim": "true",
            "access.token.claim": "true"
          }
        },
        {
          "name": "client roles",
          "protocol": "openid-connect",
          "protocolMapper": "oidc-usermodel-client-role-mapper",
          "consentRequired": false,
          "config": {
            "user.attribute": "foo",
            "introspection.token.claim": "true",
            "access.token.claim": "true",
            "id.token.claim": "true",
            "userinfo.token.claim": "true",
            "claim.name": "resource_access.\${client_id}.roles",
            "jsonType.label": "String",
            "multivalued": "true"
          }
        }
      ]
    },
    {
      "name": "email",
      "description": "OpenID Connect built-in scope: email",
      "protocol": "openid-connect",
      "attributes": {
        "include.in.token.scope": "true",
        "consent.screen.text": "\${emailScopeConsentText}",
        "display.on.consent.screen": "true"
      },
      "protocolMappers": [
        {
          "name": "email",
          "protocol": "openid-connect",
          "protocolMapper": "oidc-usermodel-attribute-mapper",
          "consentRequired": false,
          "config": {
            "introspection.token.claim": "true",
            "userinfo.token.claim": "true",
            "user.attribute": "email",
            "id.token.claim": "true",
            "access.token.claim": "true",
            "claim.name": "email",
            "jsonType.label": "String"
          }
        },
        {
          "name": "email verified",
          "protocol": "openid-connect",
          "protocolMapper": "oidc-usermodel-property-mapper",
          "consentRequired": false,
          "config": {
            "introspection.token.claim": "true",
            "userinfo.token.claim": "true",
            "user.attribute": "emailVerified",
            "id.token.claim": "true",
            "access.token.claim": "true",
            "claim.name": "email_verified",
            "jsonType.label": "boolean"
          }
        }
      ]
    },
    {
      "name": "offline_access",
      "description": "OpenID Connect built-in scope: offline_access",
      "protocol": "openid-connect",
      "attributes": {
        "consent.screen.text": "\${offlineAccessScopeConsentText}",
        "display.on.consent.screen": "true"
      }
    }
  ],
  "clients": [
    {
      "clientId": "$(json_escape "${KEYCLOAK_API_CLIENT_ID}")",
      "enabled": true,
      "protocol": "openid-connect",
      "bearerOnly": true
    },
    {
      "clientId": "$(json_escape "${KEYCLOAK_WEB_CLIENT_ID}")",
      "enabled": true,
      "protocol": "openid-connect",
      "publicClient": true,
      "directAccessGrantsEnabled": false,
      "standardFlowEnabled": true,
      "defaultClientScopes": [
        "web-origins",
        "acr",
        "roles",
        "profile",
        "basic",
        "email"
      ],
      "optionalClientScopes": [
        "offline_access"
      ],
      "redirectUris": [
        "http://localhost:${WEB_HTTP_PORT}/*",
        "http://127.0.0.1:${WEB_HTTP_PORT}/*"
      ],
      "webOrigins": [
        "http://localhost:${WEB_HTTP_PORT}",
        "http://127.0.0.1:${WEB_HTTP_PORT}",
        "+"
      ],
      "attributes": {
        "pkce.code.challenge.method": "S256",
        "post.logout.redirect.uris": "+"
      }
    },
    {
      "clientId": "$(json_escape "${KEYCLOAK_TESTER_CLIENT_ID}")",
      "enabled": true,
      "protocol": "openid-connect",
      "clientAuthenticatorType": "client-secret",
      "secret": "$(json_escape "${KEYCLOAK_TESTER_CLIENT_SECRET}")",
      "publicClient": false,
      "directAccessGrantsEnabled": true,
      "standardFlowEnabled": false,
      "defaultClientScopes": [
        "acr",
        "roles",
        "profile",
        "basic",
        "email"
      ],
      "optionalClientScopes": [
        "offline_access"
      ]
    },
    {
      "clientId": "$(json_escape "${KEYCLOAK_WATCHDOG_CLIENT_ID}")",
      "enabled": true,
      "protocol": "openid-connect",
      "publicClient": true,
      "directAccessGrantsEnabled": false,
      "standardFlowEnabled": true,
      "defaultClientScopes": [
        "web-origins",
        "acr",
        "roles",
        "profile",
        "basic",
        "email"
      ],
      "optionalClientScopes": [
        "offline_access"
      ],
      "redirectUris": [
        "https://localhost:${WATCHDOG_HTTPS_PORT}/*",
        "https://127.0.0.1:${WATCHDOG_HTTPS_PORT}/*"
      ],
      "webOrigins": [
        "https://localhost:${WATCHDOG_HTTPS_PORT}",
        "https://127.0.0.1:${WATCHDOG_HTTPS_PORT}"
      ],
      "attributes": {
        "post.logout.redirect.uris": "+"
      }
    },
    {
      "clientId": "$(json_escape "${KEYCLOAK_BACKGROUND_JOBS_CLIENT_ID}")",
      "enabled": true,
      "protocol": "openid-connect",
      "publicClient": true,
      "directAccessGrantsEnabled": false,
      "standardFlowEnabled": true,
      "defaultClientScopes": [
        "web-origins",
        "acr",
        "roles",
        "profile",
        "basic",
        "email"
      ],
      "optionalClientScopes": [
        "offline_access"
      ],
      "redirectUris": [
        "https://localhost:${BACKGROUND_JOBS_HTTPS_PORT}/*",
        "https://127.0.0.1:${BACKGROUND_JOBS_HTTPS_PORT}/*"
      ],
      "webOrigins": [
        "https://localhost:${BACKGROUND_JOBS_HTTPS_PORT}",
        "https://127.0.0.1:${BACKGROUND_JOBS_HTTPS_PORT}"
      ],
      "attributes": {
        "post.logout.redirect.uris": "+"
      }
    }
  ],
  "users": [
    {
      "username": "$(json_escape "${KEYCLOAK_DEMO_ADMIN_USERNAME}")",
      "enabled": true,
      "emailVerified": true,
      "firstName": "$(json_escape "${KEYCLOAK_DEMO_ADMIN_FIRST_NAME}")",
      "lastName": "$(json_escape "${KEYCLOAK_DEMO_ADMIN_LAST_NAME}")",
      "email": "$(json_escape "${KEYCLOAK_DEMO_ADMIN_EMAIL}")",
      "credentials": [
        {
          "type": "password",
          "value": "$(json_escape "${KEYCLOAK_DEMO_ADMIN_PASSWORD}")",
          "temporary": false
        }
      ],
      "realmRoles": [
        "ngb-admin",
        "ngb-user"
      ]
    }
  ]
}
EOF

printf 'Rendered Keycloak realm import to %s\n' "$output_file"
