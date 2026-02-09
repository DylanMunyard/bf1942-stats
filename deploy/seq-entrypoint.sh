#!/bin/bash
set -e

if [ -z "$SEQ_ADMIN_PASSWORD" ]; then
  echo "ERROR: SEQ_ADMIN_PASSWORD environment variable is not set"
  exit 1
fi

# Hash the plain-text password using Seq's built-in tool
echo "Hashing admin password..."
SEQ_FIRSTRUN_ADMINPASSWORDHASH=$(echo "$SEQ_ADMIN_PASSWORD" | seq config show-password-hash)
export SEQ_FIRSTRUN_ADMINPASSWORDHASH

# Clear the plain-text password from the environment
unset SEQ_ADMIN_PASSWORD

echo "Admin password hash set. Starting Seq..."
exec /run.sh
