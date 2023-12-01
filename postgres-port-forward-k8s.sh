#!/usr/bin/env bash

set -euo pipefail

# Enable access to K8s postgres db from host machine's postgres client
export POSTGRES_PASSWORD=$(kubectl get secret -n akkabank pg-postgresql -o jsonpath="{.data.password}" | base64 -d)
minikube kubectl -- port-forward -n akkabank svc/pg-postgresql 5432:5432 & PGPASSWORD="$POSTGRES_PASSWORD" psql --host 127.0.0.1 -U testuser -d akkabank -p 5432
