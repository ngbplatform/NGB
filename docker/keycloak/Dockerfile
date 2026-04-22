ARG KEYCLOAK_VERSION=26.5.6

FROM quay.io/keycloak/keycloak:${KEYCLOAK_VERSION} AS builder

ENV KC_DB=postgres \
    KC_HEALTH_ENABLED=true \
    KC_METRICS_ENABLED=true \
    KC_HTTP_MANAGEMENT_HEALTH_ENABLED=true

COPY ui/ngb-auth-theme /opt/keycloak/themes/ngb-auth
COPY --chmod=0755 docker/keycloak/render-realm.sh /opt/keycloak/bootstrap/render-realm.sh
COPY --chmod=0755 docker/keycloak/import-realm.sh /opt/keycloak/bootstrap/import-realm.sh

RUN /opt/keycloak/bin/kc.sh build

FROM quay.io/keycloak/keycloak:${KEYCLOAK_VERSION}

COPY --from=builder /opt/keycloak /opt/keycloak

ENTRYPOINT ["/opt/keycloak/bin/kc.sh"]
