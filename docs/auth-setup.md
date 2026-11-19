# Authentication setup

Sign-in goes through Keycloak only, and it's invite-only: self-registration is off, so nobody can create their own
account. You bootstrap the first admin by hand (below); everyone else is invited from the app.

- [Local services](#local-services)
- [What the realm import creates](#what-the-realm-import-creates)
- [Client settings](#client-settings)
- [Client secrets](#client-secrets)
- [Bootstrapping the first admin](#bootstrapping-the-first-admin)
- [Inviting users](#inviting-users)
- [Re-importing the realm](#re-importing-the-realm)

## Local services

From the repo root:

```bash
docker compose up -d
```

| Service | URL / port | Notes |
| --- | --- | --- |
| App Postgres | `localhost:5432` | database `helix`, user `helix` / `dev_password` |
| Keycloak Postgres | `localhost:5433` | used by Keycloak only |
| Keycloak | `http://localhost:8080` | admin console at `/admin`, bootstrap admin `admin` / `admin` |
| MailHog | `http://localhost:8025` | catches every email Keycloak sends; SMTP on `1025` |

The bootstrap admin comes from `KC_BOOTSTRAP_ADMIN_USERNAME` and `KC_BOOTSTRAP_ADMIN_PASSWORD`, which default to
`admin` / `admin`. Set them in your shell or a root `.env` file to change them. All of these passwords are local-only
defaults.

The OIDC issuer is `http://localhost:8080/realms/helix`. Keycloak runs with `KC_HOSTNAME=http://localhost:8080` and
`KC_HOSTNAME_BACKCHANNEL_DYNAMIC=true`, so the issuer stays the same for both browsers and containers reaching
Keycloak at `keycloak:8080`.

## What the realm import creates

On its first start, Keycloak imports [keycloak/realm-export.json](../keycloak/realm-export.json) (`start-dev
--import-realm`). The app's URL and the SMTP settings in it are placeholders with local defaults
(`${APP_URL:http://localhost:3000}`, `${SMTP_HOST:mailhog}`...), which Keycloak fills from its environment at import,
so a deployed realm gets the production values. It creates the realm `helix` with:

**Realm settings**

- User registration **off**, so accounts only come from invitations or the admin console
- Verify email on, forgot password on, login with email on, duplicate emails not allowed
- Brute-force protection on; passwords need at least 12 characters and can't match the username or email
- SMTP pointing at MailHog (`mailhog:1025`), from `noreply@helix.local`

**Realm roles**

| Role | Meaning |
| --- | --- |
| `helix-user` | Can create and run experiments. A **default role**, so every user gets it when they're created, including invited users; nothing has to assign it. |
| `helix-admin` | Manages invitations and can read every user's experiments. Assigned by hand. |

**Clients:** `helix-web`, `helix-api` and `helix-eda`, set up as in [Client settings](#client-settings).

## Client settings

The import sets these, so locally there is nothing to do. Use the tables to check a realm, or to set the clients up by
hand on a Keycloak that doesn't import `realm-export.json`. Create the realm and roles first. The setting names are
the ones in the Keycloak admin console. Replace `http://localhost:3000` with the app's URL when deployed.

**`helix-web`**: confidential client used by the Next.js server (Auth.js) to sign users in.

| Setting | Value |
| --- | --- |
| Client authentication | **On** |
| Authentication flow | **Standard flow** only (Direct access grants, Implicit flow and the rest off) |
| Valid redirect URIs | `http://localhost:3000/*` |
| Valid post logout redirect URIs | `http://localhost:3000/*` (federated sign-out returns here) |
| Web origins | `http://localhost:3000` |
| Advanced → Proof Key for Code Exchange Code Challenge Method | `S256` |

Under **Client scopes → `helix-web-dedicated` → Add mapper → By configuration**, add two mappers:

| Mapper type | Name | Settings |
| --- | --- | --- |
| Audience | `helix-api audience` | Included Client Audience `helix-api`; Add to access token **on**; Add to ID token off |
| User Realm Role | `realm roles` | Token Claim Name `realm_access.roles`; Multivalued on; Add to ID token and Add to access token **on** |

The audience mapper puts `helix-api` in the access token's `aud`, so the API accepts tokens issued to the web app. The
realm roles mapper makes sure the roles are in both tokens.

**`helix-api`**: confidential client for the ASP.NET Core API. It's the JWT audience for user tokens, and its service
account calls the Keycloak admin API to send invites and calls the EDA service.

| Setting | Value |
| --- | --- |
| Client authentication | **On** |
| Authentication flow | **Service accounts roles** only (Standard flow off: the API never signs users in) |
| Service accounts roles tab | **Assign role → Filter by clients**: `realm-management` `manage-users`, `view-users` and `query-users`, nothing else |

Under **Client scopes → `helix-api-dedicated` → Add mapper → By configuration**, add one mapper:

| Mapper type | Name | Settings |
| --- | --- | --- |
| Audience | `helix-eda audience` | Included Client Audience `helix-eda`; Add to access token **on**; Add to ID token off |

That mapper is why `helix-eda` exists: it puts `helix-eda` in the `aud` claim of the API's service-account (client
credentials) tokens, so the EDA service — which only ever sees the API, never a browser — can verify that a bearer
token was actually meant for it, the same way `helix-api` verifies tokens meant for the API.

`manage-users`, `view-users` and `query-users` are enough to create users, look them up and send them action emails
(invites), and nothing more: the service account can't read or change clients or realm settings. It doesn't need
permission to assign roles, because `helix-user` is a default role.

**`helix-eda`**: confidential client that exists only as a token audience. It has no enabled flows and no service
account: nothing ever signs in as `helix-eda` or gets a token issued to it directly. The EDA service only ever
validates tokens that `helix-api`'s service account obtained with `helix-eda` in their `aud` (#16); it never issues or
exchanges tokens of its own.

## Client secrets

Keycloak generates all client secrets during the import; they aren't in the repo. To read one, open the admin
console, switch to the `helix` realm, go to **Clients → `<client>` → Credentials**, and copy the **Client secret**.
`helix-eda` has a secret too (client authentication is on), but nothing in this repo needs it: no service signs in as
`helix-eda`.

| Secret | Where it goes |
| --- | --- |
| `helix-api` | the API's configuration, `Keycloak:Secret` (#5) |
| `helix-web` | the web app's configuration, `AUTH_KEYCLOAK_SECRET` (#7) |

**To regenerate a secret**, select **Regenerate** on the same Credentials tab, then update the API or web app's
configuration and restart it. The old secret stops working straight away. Re-importing the realm (below) also
generates new secrets.

## Bootstrapping the first admin

The app can only send invites once an admin exists, so you create the first admin by hand:

1. Start the services with `docker compose up -d` and wait for Keycloak to come up.
2. Sign in at `http://localhost:8080/admin` with the bootstrap admin (`admin` / `admin` unless you changed it).
3. Switch to the **`helix`** realm with the realm selector, top left. Don't create the user in `master`.
4. **Users → Create new user.** Set **Username** and **Email** to your email address, turn **Email verified** on (or
   Keycloak will ask you to verify it at first sign-in, through MailHog), fill in your name, and select **Create**.
5. **Credentials → Set password.** Use at least 12 characters and turn **Temporary** off.
6. **Role mapping → Assign role.** Filter by realm roles, tick **`helix-admin`** and select **Assign**. `helix-user`
   is already there as a default role.
7. Start the API and web app, open `http://localhost:3000` and sign in with your email and password.

From then on, invite other people from the app's **Users** page ([below](#inviting-users)). The bootstrap admin is
only for the Keycloak admin console; it can't sign in to the app, because it lives in the `master` realm.

## Inviting users

Admins invite colleagues from the web app (#6, #8): an admin-only page calls the API's invitations endpoints, which
use the `helix-api` service account to create a disabled-until-set-password Keycloak user and send them a "set your
password" action email. Nobody can get an account any other way, because self-registration is off. The invited user
gets `helix-user` automatically (the default role); an admin assigns `helix-admin` afterwards in the Keycloak admin
console, or the app does, once that's built.

## Re-importing the realm

`--import-realm` skips a realm that already exists, so changes to `realm-export.json` only apply to a fresh Keycloak
database. The simplest way to pick them up is `docker compose down -v && docker compose up -d` (see
[local-development.md](local-development.md#resetting-data)), which wipes every service's data, not just Keycloak's.

To reset only Keycloak and keep the app database:

```bash
docker compose rm -sf keycloak postgres-keycloak
docker volume rm "$(docker compose ps --format '{{.Project}}')_postgres-keycloak-data"
docker compose up -d
```

Either way, this deletes every Keycloak user, including your admin, and generates new client secrets. Bootstrap the
admin again and update the secrets afterwards.
