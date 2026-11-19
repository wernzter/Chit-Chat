#!/bin/sh
# Prefer the same local identity on every rebuild. A new certificate is a new TCC identity.
# Explicit SIGN_ID overrides remain available for distribution builds.
set -eu
security find-identity -v -p codesigning 2>/dev/null | awk '
    /"Chit Chat Local Development"/ { local_id = $2 }
    /"Developer ID Application:/ && !developer_id { developer_id = $2 }
    END { if (local_id) print local_id; else if (developer_id) print developer_id }
'
