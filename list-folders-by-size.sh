#!/bin/bash

# List folders by size in descending order
# Usage: ./list-folders-by-size.sh [optional-path]

TARGET_DIR="${1:-.}"

if [ ! -d "$TARGET_DIR" ]; then
    echo "Error: Directory '$TARGET_DIR' not found"
    exit 1
fi

du -sh "$TARGET_DIR"/* 2>/dev/null | sort -hr
