ARG MINIO_IMAGE_REPOSITORY=ghcr.io/coollabsio/minio
ARG MINIO_IMAGE_TAG=RELEASE.2025-10-15T17-29-55Z
ARG MINIO_IMAGE_DIGEST=sha256:69b55a1c1c5dc285ce04db96689f5b2102317fc77a50680a1874ca6efd1c87f9

FROM ${MINIO_IMAGE_REPOSITORY}:${MINIO_IMAGE_TAG}@${MINIO_IMAGE_DIGEST}

COPY --chmod=0755 docker/minio/init.sh /bootstrap/bootstrap-attachments.sh

CMD ["server", "/data", "--console-address", ":9001"]
