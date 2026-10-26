# Fleet server

Run a fleet server of your own, so every Avala instance on your team sends it what its jobs do and the Fleet page shows spend and tokens across all of them.

## What it does

- **One server for the whole fleet.** Each computer running Avala is an instance with its own key. Once connected, it sends the server each job's repository, statuses, model, token usage, cost and verification, and never a path, a title, an instruction, a prompt or a file name.
- **The Fleet page** in Avala reads it back: spend and tokens over a range, and how they split across repositories, instances and models. Every figure says how it changed since the previous range of the same length, tokens are stacked by type, and every list row says its part of the total. Choose a repository to see only its figures and its jobs. When personal details are hidden, **Show names** reveals them on this page until you leave it.
- **Ready to run with Docker.** `deploy/fleet/` in the repository holds three files that work together:
  - **`compose.yaml`:** PostgreSQL 18.3 on a volume, the server, and Caddy, which gets and renews an HTTPS certificate for your domain. Only Caddy is reachable from outside.
  - **`.env.example`:** the five values you fill in.
  - **`fleet`:** a small command to register and revoke instances, and to back up and restore the database.
- **Two kinds of key, neither handed out by anyone else:**
  - **The administrator key** is one you make yourself when you install the server and keep in its `.env`. It only registers and revokes computers, and never leaves the server.
  - **An instance key**, `avk_` and 43 more characters, is made by your server for each computer you register. Avala keeps it in that computer's keychain, never in a file, and sends it only to your server.
- **HTTPS only.** Avala sends its key only over `https`, so the server is always behind Caddy. On a network without a public domain, Caddy signs with its own certificate authority, and each computer trusts it once.
- **Proven before each release.** `scripts/fleet-deploy-check.cs` runs this compose and checks, end to end:
  - a real Avala connecting over HTTPS;
  - an upgrade from the previous version;
  - restarts;
  - a backup, then deleting every volume, then a restore;
  - that no key reaches any log.

## How to use it

1. **Prepare a machine** with Docker and Docker Compose, ports 80 and 443 open, and a DNS name that points to it, such as `fleet.example.com`.
2. **Copy the files** of `deploy/fleet/` to it, and create `.env` from `.env.example`:

   ```sh
   cp .env.example .env
   chmod 600 .env
   ```

   Fill in the version, your domain, an e-mail for the certificate, an administrator key (`openssl rand -base64 48`) and a database password (`openssl rand -hex 24`).
3. **Start it:** `docker compose up -d`. After a minute, `https://<your domain>/health` answers `Healthy`.
4. **Register a computer:** `./fleet register "Rick's laptop"`. It prints the instance's key once; copy it and give it to that computer's person privately, as you would a password.
5. **Connect Avala** on that computer: open **Settings**, **Fleet**, write `https://<your domain>` and the key, and choose **Connect**. Settings shows **Connected as** the name you registered.
6. **Open the Fleet page** in the sidebar once a job has run.

**On a network without a public domain:**

1. Set `AVALA_FLEET_DOMAIN` to the name the computers use, such as `fleet.lan`, and `AVALA_FLEET_TLS=internal`.
2. Run `./fleet root-certificate > fleet-root.crt` and trust that file on each computer:
   - **Linux:** `sudo trust anchor fleet-root.crt`.
   - **macOS:** `sudo security add-trusted-cert -d -r trustRoot -k /Library/Keychains/System.keychain fleet-root.crt`.
   - **Windows:** `certutil -addstore Root fleet-root.crt`, as administrator.

## Settings and files

**`.env`**, beside `compose.yaml`:

```sh
AVALA_SERVER_VERSION=0.1.0
AVALA_FLEET_DOMAIN=fleet.example.com
AVALA_FLEET_TLS=you@example.com
AVALA_ADMINISTRATOR_KEY=at-least-32-characters
AVALA_DATABASE_PASSWORD=a-long-random-password
```

- Write each value without quotes. Make the database password with `openssl rand -hex 24`: a `;` or a `$` in it would break the connection the server opens.
- `AVALA_SERVER_VERSION`: the release of `ghcr.io/rick-dev-creator/avala-server` to run. Images are published for `linux/amd64` and `linux/arm64`.
- `AVALA_FLEET_TLS`: an e-mail for Let's Encrypt, or `internal` for Caddy's own certificate authority.
- `AVALA_ADMINISTRATOR_KEY`: registers and revokes instances. The server refuses to start with one shorter than 32 characters.
- `AVALA_HTTP_PORT` and `AVALA_HTTPS_PORT`: other ports than 80 and 443, when those are taken.
- `AVALA_SERVER_IMAGE`: another image than GHCR's, such as one you built yourself.

**`./fleet`:**

| Command | Does |
| --- | --- |
| `./fleet register <name>` | Registers an instance and prints its key, once |
| `./fleet show <id>` | Shows an instance and whether it is revoked |
| `./fleet revoke <id>` | Revokes an instance; its key stops working at once |
| `./fleet backup <file>` | Writes a backup of the database |
| `./fleet restore <file>` | Replaces the database with a backup, stopping the server meanwhile |
| `./fleet root-certificate` | Prints the root certificate of the internal mode |

## Good to know

- **Updating:**
  1. Take a backup first: `./fleet backup before-0.2.0.dump`.
  2. Change `AVALA_SERVER_VERSION` in `.env`.
  3. Run `docker compose pull`, then `docker compose up -d`.

  The server applies its database migrations when it starts. Instance keys and everything recorded are kept.
- **Order and restarts:**
  - The server starts once PostgreSQL is ready, and every service restarts by itself unless you stop it.
  - While the server restarts, Caddy holds requests for up to 30 seconds instead of failing them.
- **Health:** `/health` answers 503 while the server cannot reach its database; `/alive` only says the server runs.
- **Logs:** `docker compose logs server`. No key is ever written to them.
- **Not done yet:**
  - Instances cannot be listed; keep the ids `./fleet register` prints.
  - There is no web interface: the Fleet page in Avala is how you read the fleet.
  - Only one server may run against a database, since it migrates at start.
- **A lost instance key cannot be shown again:** the server keeps only a hash of it. Revoke it with `./fleet revoke <id>` and register the computer again. Revoke a computer the same way when someone leaves or loses it; its key stops working at once.
- **Avala says the certificate is not trusted:** on a public domain, check that the domain points to the server and that ports 80 and 443 are open, so Caddy can get its certificate. In the internal mode, trust `fleet-root.crt` on that computer.
- **`docker compose pull` says `unauthorized`:** the image package on GHCR is not public yet.

## See also

- [Privacy and usage statistics](privacy-and-usage-statistics.md): what a fleet receives, and when.
- [Releasing](../release.md#the-fleet-server-image): how the image is built and published.
- [Architecture](../architecture.md#the-fleet-server): how the server is built.
