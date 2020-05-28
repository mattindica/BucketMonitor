ARG base_image
FROM $base_image
LABEL maintainer="Indica Labs, Inc"

RUN apk add --no-cache libstdc++ libintl

VOLUME /mnt/bucketmonitor
WORKDIR app
COPY . .

ENTRYPOINT ["./BucketMonitor", "monitor"]