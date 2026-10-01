#!/bin/sh
set -e
psql "$STOREOS_MIGRATIONS_CONNECTION_URI" -v ON_ERROR_STOP=1 -f deploy/db/post-migrate.sql
