# syntax=docker/dockerfile:1
# Mirats / AspireReact - encrypted Postgres backup sidecar.
#
# Base image: official `postgres:18` (PostgreSQL License) - it already ships pg_dump/pg_restore
# matching the database server version. Only Debian packages are added:
#   - age (BSD-3-Clause)          public-key encryption (private key stays OFF the server)
#   - rsync (GPL-3.0)             optional off-site copy
#   - openssh-client (BSD)        SSH transport for rsync/scp
#   - tzdata, ca-certificates     schedule in Asia/Ho_Chi_Minh + TLS for remote copy
# No third-party container image is used.

FROM postgres:18

RUN apt-get update \
    && apt-get install -y --no-install-recommends \
        age \
        rsync \
        openssh-client \
        tzdata \
        ca-certificates \
    && rm -rf /var/lib/apt/lists/*

COPY backup.sh /usr/local/bin/backup.sh
RUN chmod 0755 /usr/local/bin/backup.sh

ENV TZ=Asia/Ho_Chi_Minh
ENTRYPOINT ["/usr/local/bin/backup.sh"]
