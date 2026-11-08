# Deploying to the Raspberry Pi

Two environments run on the Pi, each a systemd service behind nginx with its own user and database:

| | stage | prod |
|---|---|---|
| URL | https://sperantatm-api-stage.thorsp.net | https://sperantatm-api.thorsp.net |
| Deployed by | a green `ci.yml` run of a push to `main`, if it's still main's tip | a green `release.yml` run for a `vX.Y.Z` release on `main`, newer than the live one |
| Service / user | `speranta-api-stage` as `speranta-stage` (127.0.0.1:5095) | `speranta-api` as `speranta-prod` (127.0.0.1:5094) |
| Database | `speranta_stage` | `speranta_prod` |
| Web client | https://staging.speranta-web.pages.dev (and PR previews) | https://sperantatm.thorsp.net |

So: merge a PR and it is on **stage** a few minutes later. When stage looks right, publish a
**GitHub Release** `vX.Y.Z` from `main` and it goes to **prod**.

## How a deploy happens

There is **no self-hosted runner**: the repo is public, and a Pi runner that public repos can use
would let a fork PR run code on the Pi. Instead the Pi pulls:

1. `ci.yml` finishes on `main`, or `release.yml` finishes after a release is published.
2. GitHub sends a signed `workflow_run` webhook to the Pi. It checks the signature (its own secret),
   accepts only a successful run of one of those two workflow **files** in this repo, and starts
   `speranta-deploy`.
3. [`speranta-deploy`](speranta-deploy) trusts nothing it was given. It re-reads the run from GitHub
   (repo, workflow file, event, ref, conclusion, commit), deploys stage only when the commit is still
   main's tip, and prod only for a release that isn't a pre-release or draft, whose commit is on
   `main`, and that is newer (in version order) than the live one. For prod it also verifies both
   files' **build attestation**: signed by GitHub for exactly `release.yml` at that tag and commit, so
   a file swapped into the release afterwards is refused.
4. It installs the build into a staging folder (root-owned, read-only for the app), runs the
   migrations bundle as a sandboxed one-off unit under the environment's user, moves the release into
   `releases/<version>`, switches `current`, restarts the service and checks `/health` (which touches
   the database) and that the process really runs from the new folder. If that fails it goes back to
   the release that was live before.
5. Failures, rollbacks and prod deploys are posted to Discord.

Nothing is built on the Pi: CI builds on GitHub's arm64 runner and runs the same migrations bundle
against an empty database before the Pi ever sees it.

The script is installed **by hand** and never by a webhook, so a bad build can't change how deploys
run. After changing it, install it again:

```bash
sudo install -m 755 deploy/pi/speranta-deploy /usr/local/bin/speranta-deploy
```

## Operating

```bash
speranta-deploy rollback stage                      # back to the healthy release before the live one
speranta-deploy stage <ci-run-id> <sha> --force     # deploy a green main run that isn't the tip
speranta-deploy prod <release-run-id> vX.Y.Z --force   # redeploy an older release
journalctl -u speranta-api-stage -f                 # the service's own output (also in /var/log/syslog)
```

- **A rollback restores code, not schema.** Migrations must keep working with the previous release:
  add first, remove in a later release.
- Each environment keeps a history (`/srv/speranta/apps/api-<env>/history`) of the releases that
  passed their checks. Rollback walks it backwards and never picks a release that failed. The newest
  five healthy releases are kept.
- Every run appends to `~/.local/state/speranta-deploy/deploy.log` of the user running it; pi-control's
  `logs/speranta_deploy.log` also has the webhook-started ones.
- **Settings that change without a deploy** (CORS origins, the first admin, SMTP) live in the env
  files below: edit, then `sudo systemctl restart speranta-api[-stage]`.

## What the app keeps

`/srv/speranta/data/<env>/` (owned by the environment's user, 0700), outside the releases:

- `files/`: uploads (flyers, announcement covers), `Storage__Path`.
- `dp-keys/`: ASP.NET Data Protection keys, `DataProtection__KeyPath`. They sign the admins' tokens
  and encrypt the Gmail app password saved from the admin. Losing them signs every admin out and
  makes the saved password unreadable (paste it again in the admin).

Both are backed up nightly with the Pi's other data, and the databases are dumped with them.

## Isolation

- Each environment has its own Unix user, so stage code can't read prod's process or files.
- Releases are owned by root and read-only for the app. The app can write only its data folder.
- The env files `/srv/speranta/.config/speranta/api-{stage,prod}.env` are root-only (0600). systemd
  reads them before dropping privileges, both for the service and for the migration unit, so the
  database password never appears in a command line.
- The units carry systemd's sandboxing (`ProtectSystem=strict`, `SystemCallFilter=@system-service`,
  no capabilities, and so on); `MemoryDenyWriteExecute` is left out because .NET's JIT needs it off.
  `/srv`, `/mnt` and `/nas` are hidden except the environment's own releases (read-only) and data
  folder. The migration unit gets the same sandbox.
- **Network guard** ([`netguard.nft`](netguard.nft); loaded by
  [`speranta-netguard.service`](speranta-netguard.service), again by each unit before it starts, and
  by the deploy right before migrations): **deny by default**. The two app users may only answer
  connections made to them (nginx), reach their Postgres on 127.0.0.1:5436, and use the egress proxy
  on 127.0.0.1:8898. No other local service, no LAN or tailnet, and no public internet either: the
  Pi's own public IP comes back through the router's hairpin NAT looking like LAN traffic, so "public
  only" isn't safe. The units also drop `AF_UNIX`.

## Egress proxy

The API needs two outside services: **Gmail's SMTP** (the notifications) and the **Web Push**
services (the admins' phone alerts). It reaches them only through its own tinyproxy instance,
[`speranta-egress.service`](speranta-egress.service) on `127.0.0.1:8898` as user `speranta-egress`
([`egress.conf`](egress.conf)): CONNECT only, to ports 443 and 587, default-deny, to the hosts in
[`egress-allow`](egress-allow). The units set `HTTPS_PROXY=http://127.0.0.1:8898`; .NET's
`HttpClient` (Web Push) follows it by itself, and the SMTP client takes the same proxy through
`SmtpProxy` (`Email/Mail.cs`). The proxy is confined by the guard too (DNS on Tailscale's resolver and
ports 443/587 to public addresses only) and logs every tunnel as `CONNECT host:port` to syslog.

**A new host is a manual install**, like the units: add one `^host\.name:port$` line to
`egress-allow` in the same PR as the feature, then on the Pi
`sudo install -m 644 deploy/pi/egress-allow /etc/speranta/egress-allow && sudo systemctl restart speranta-egress`.
Changing `Email:Host` to another mail provider needs its line too. Don't widen the guard instead.

## One-time setup (done; for a new machine)

- Users `speranta-stage`, `speranta-prod` and `speranta-egress` (system, no shell, no home).
- `/srv/speranta/apps/api-{stage,prod}/` (root), `data/{stage,prod}/{files,dp-keys}` (owned by each
  user, 0700), `.config/speranta/` (root, 0700).
- Postgres: [`postgres.compose.yml`](postgres.compose.yml) at `/srv/speranta/postgres/compose.yml`,
  bound to `127.0.0.1:5436`, with a role and database per environment.
- Env files, root 0600, one per environment: `ConnectionStrings__Postgres` (quoted: it contains
  semicolons), `Cors__Origins__0…` / `Cors__OriginSuffixes__0`, `Email__WebUrl`,
  `Bootstrap__AdminEmails__0`, and the SMTP settings once there is an app password (it can also be
  pasted in the admin). The non-secret paths and the proxy are in the units.
- `/etc/speranta/{netguard.nft,egress.conf,egress-allow}` and the units
  [`speranta-netguard.service`](speranta-netguard.service),
  [`speranta-egress.service`](speranta-egress.service), [`speranta-api.service`](speranta-api.service),
  [`speranta-api-stage.service`](speranta-api-stage.service) in `/etc/systemd/system/`. The API units
  stay disabled until their first successful deploy enables them.
- **The units are installed by hand, like the deploy script; a deploy never touches them.** After
  changing one: `sudo install -m 644 deploy/pi/speranta-*.service /etc/systemd/system/ && sudo systemctl daemon-reload`
  (the new settings apply at the next restart or deploy).
- nginx: [`nginx-speranta.conf`](nginx-speranta.conf) as `/etc/nginx/sites-available/speranta`
  (DNS: CNAMEs to `thorsp.net`; TLS by certbot, cert `speranta`).
- The repo webhook (event `workflow_run`) pointing at pi-control's `/gh/speranta-deploy`, with its
  own secret.
