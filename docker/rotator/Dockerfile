FROM alpine:3.21

RUN apk add --no-cache bash curl jq postgresql-client coreutils openssl dcron

COPY docker/rotator/rotate-password.sh /usr/local/bin/rotate-password.sh
COPY docker/rotator/entrypoint.sh /usr/local/bin/entrypoint.sh
RUN chmod +x /usr/local/bin/rotate-password.sh /usr/local/bin/entrypoint.sh

ENTRYPOINT ["/usr/local/bin/entrypoint.sh"]
