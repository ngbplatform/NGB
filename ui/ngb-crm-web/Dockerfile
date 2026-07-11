# syntax=docker/dockerfile:1.7

FROM node:22.17-alpine AS build
ARG NGB_PLATFORM_UI_TARBALL
WORKDIR /workspace/ui/ngb-crm-web

COPY ui/ngb-crm-web/package.json ui/ngb-crm-web/package-lock.json ./
COPY artifacts/npm/ /workspace/artifacts/npm/

RUN --mount=type=cache,target=/root/.npm \
    if [ -n "$NGB_PLATFORM_UI_TARBALL" ]; then \
      npm install --no-save --package-lock=false "/workspace/$NGB_PLATFORM_UI_TARBALL"; \
    else \
      npm ci; \
    fi

COPY ui/postcss.shared.config.js ui/tailwind.shared.config.js /workspace/ui/
COPY ui/ngb-crm-web ./

RUN npm run build

FROM nginxinc/nginx-unprivileged:1.29-alpine AS final
USER root

RUN apk add --no-cache gettext

COPY ui/ngb-crm-web/docker/nginx.conf /etc/nginx/conf.d/default.conf
COPY ui/ngb-crm-web/docker/runtime-config.js.template /etc/nginx/templates/runtime-config.js.template
COPY ui/ngb-crm-web/docker/entrypoint.sh /docker-entrypoint-custom.sh
COPY --from=build /workspace/ui/ngb-crm-web/dist /usr/share/nginx/html

RUN chmod +x /docker-entrypoint-custom.sh \
  && chown -R nginx:nginx /usr/share/nginx/html /etc/nginx/conf.d /etc/nginx/templates /docker-entrypoint-custom.sh

USER nginx

EXPOSE 8080

HEALTHCHECK --interval=30s --timeout=3s --start-period=10s --retries=3 CMD wget -q -O /dev/null http://127.0.0.1:8080/healthz || exit 1

ENTRYPOINT ["/docker-entrypoint-custom.sh"]
