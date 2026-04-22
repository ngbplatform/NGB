ARG POSTGRES_IMAGE_TAG=18.3-alpine

FROM postgres:${POSTGRES_IMAGE_TAG}

COPY --chmod=0755 docker/postgres/init/010-create-application-databases.sh /docker-entrypoint-initdb.d/010-create-application-databases.sh
