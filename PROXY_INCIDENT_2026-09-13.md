# Shared API 403 incident — 13 September 2026

## Diagnosis

- Protected requests returned `403 {"error":"Access forbidden"}` while authentication routes remained reachable.
- API logs showed `Blocked request from IP: ::ffff:10.0.1.6`.
- Docker network inspection confirmed that `10.0.1.6` was `coolify-proxy` on the `coolify` network. The API had no `ReverseProxy__*` environment variables.
- The block was already inactive when work resumed on 13 September. No block keys or database records were manually deleted.

## Applied configuration

Added the runtime environment variable in Coolify:

```text
ReverseProxy__KnownProxies__0=10.0.1.6
```

Used Coolify's **Restart** action (without rebuilding). The replacement container was confirmed to contain the variable; Coolify reported Finished and healthy. No application authorization or security-filter code was disabled. Existing source defaults retain a one-hop forwarding limit and header-symmetry checks.

## Verification

Unauthenticated HTTPS probes after the restart:

| Route | Status |
| --- | --- |
| `/api/health/ready` | 200 |
| `/api/directory/facilities?city=Baku&pageSize=1` | 200 |
| `/api/auth/me` | 401 |
| `/api/notifications/inbox?page=1&pageSize=1` | 401 |
| `/api/appointment/my-appointments` | 401 |
| `/api/prescription/my-prescriptions` | 401 |
| `/api/referral/my-referrals` | 401 |

These checks confirm that private routes require authentication instead of returning the shared IP block. Authenticated patient refresh on the physical iPhone still needs user verification; no patient credentials or medical records were accessed during these checks.

## Operational follow-up

If Coolify recreates its proxy or changes networking, verify the proxy address again and update the exact trusted address. Do not replace it with trust-all forwarding or trust an entire private network by default.

Read-only mapping check on the deployment host:

```sh
docker network inspect coolify --format '{{range .Containers}}{{.Name}} {{.IPv4Address}}{{println}}{{end}}'
```

The setting is deployment configuration, not a value hardcoded in application source. Keep it with the Coolify resource when migrating servers.
