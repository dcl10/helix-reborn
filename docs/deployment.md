# Deployment

This covers `infra/` only: the Terraform that creates the DigitalOcean infrastructure for v1 (one droplet running
Docker Compose). Production Compose, Caddy, the updater script and the `.env` template are #42; the GitHub workflow
that runs this Terraform is #43.

## Infrastructure

`infra/` creates:

| Resource | |
| --- | --- |
| Droplet | Ubuntu 24.04, `s-4vcpu-8gb` by default (training is CPU-heavy), monitoring on, tagged `helix-host` |
| Block-storage volume | Mounted at `/mnt/helix-data`. Holds Docker's data (both databases, images) and the checkout. Not protected: `terraform destroy` deletes it. |
| Reserved IP | The hostnames come from it, so they survive the droplet being replaced |
| Cloud firewall | SSH (22) only from `ssh_allowed_cidrs`; 80 and 443 from anywhere; ICMP |
| SSH key | Yours, for `root` |
| Project | Groups the droplet, volume, reserved IP and Spaces bucket in the control panel |
| DNS records | Only with `domain` set: `app` and `auth` A records. Without one, the hostnames are `app.<ip>.sslip.io` and `auth.<ip>.sslip.io` (sslip.io resolves them to the IP; no DNS to manage). |
| Spaces bucket | `digitalocean_spaces_bucket.app`: private, for datasets, plots and models (see [architecture.md](architecture.md#storage-layout-s3)). Not force-destroyed: tearing down the host must not delete users' data. |
| Spaces key | `digitalocean_spaces_key.app`: a key scoped to read/write only that bucket, for the app's `.env` |

On first boot, cloud-init (`infra/cloud-init.yaml.tftpl`) mounts the volume, adds 2 GB of swap, installs Docker
Engine and the Compose plugin from Docker's apt repository with its data on the volume, and clones the repo to
`/mnt/helix-data/helix-reborn`. The droplet **pulls** the app's images; it doesn't build them. If the checkout has
`deploy/init-env.sh` (added by #42), cloud-init runs it to create `.env` with the hostnames filled in. If it has
`deploy/systemd/helix-update.service` and `.timer` (also #42), cloud-init installs and enables
`helix-update.timer`, which is what actually deploys and updates the app. Until #42 lands, those steps are skipped
and the droplet just sits there with Docker installed and the repo cloned.

**Changing cloud-init** (for example editing `cloud-init.yaml.tftpl`) only takes effect on a droplet's first boot;
Terraform ignores later changes to it for an existing droplet. Apply one with
`terraform apply -replace=digitalocean_droplet.host`. That's safe: everything that matters (the checkout, `.env`,
both databases) lives on the volume, which the new droplet reattaches.

### One-time manual steps

Two things exist before this Terraform runs, and aren't managed by it:

1. **The remote state bucket.** In the DigitalOcean control panel, create a Spaces bucket for Terraform state
   (private ACL). This is separate from the app's own bucket that Terraform creates.
2. **A Spaces key with access to that bucket.** Control panel → **API** → **Spaces Keys** → generate a new key.
   Used as the backend's credentials (`AWS_ACCESS_KEY_ID` / `AWS_SECRET_ACCESS_KEY`), not the app's own Spaces key
   (`spaces_access_key_id` / `spaces_secret_key` outputs below), which Terraform creates for you.

### Variables

Set in `terraform.tfvars` (gitignored; start from `infra/terraform.tfvars.example`):

| Variable | Default | |
| --- | --- | --- |
| `name` | `"helix"` | Prefix for every resource this creates |
| `region` | `"lon1"` | DigitalOcean region for the droplet, volume, reserved IP and Spaces bucket |
| `droplet_size` | `"s-4vcpu-8gb"` | Host size |
| `data_volume_size_gb` | `50` | Size of the data volume |
| `ssh_public_key` | *(required)* | Public key allowed to SSH in as root |
| `ssh_allowed_cidrs` | *(required)* | CIDRs allowed to SSH in, e.g. `["203.0.113.7/32"]`. Never `0.0.0.0/0`. |
| `domain` | `""` | A domain in this DigitalOcean account, for `app.<domain>` / `auth.<domain>`. Empty uses sslip.io hostnames. |
| `repo_url` | `"https://github.com/dcl10/helix-reborn.git"` | Repo the droplet clones |
| `repo_ref` | `"main"` | Branch or tag the droplet checks out on first boot |
| `spaces_bucket_name` | *(required)* | Name of the app's Spaces bucket; must be globally unique across all of DigitalOcean |

### Running it locally

```bash
cd infra
cp terraform.tfvars.example terraform.tfvars     # your SSH key, allowed CIDRs and a unique bucket name
cp backend.hcl.example backend.hcl               # the state bucket's name and endpoint

export AWS_ACCESS_KEY_ID=...                     # the Spaces key with access to the state bucket
export AWS_SECRET_ACCESS_KEY=...
export DIGITALOCEAN_TOKEN=...                    # your own DigitalOcean API token
export SPACES_ACCESS_KEY_ID=...                  # an account-level Spaces key, for creating the app bucket and key
export SPACES_SECRET_ACCESS_KEY=...

terraform init -backend-config=backend.hcl
terraform plan                                   # read it
terraform apply
terraform output                                 # app_url, auth_url, ssh, admin_console_tunnel, spaces_bucket, ...
```

`backend.hcl` and `terraform.tfvars` are both gitignored; never commit either.

### Outputs

| Output | | |
| --- | --- | --- |
| `ip_address` | The host's reserved IP | |
| `app_url` | Where users use the app | |
| `auth_url` | Keycloak's public URL (sign-in only; the admin console isn't served here) | |
| `ssh` | `ssh root@<ip>` | |
| `admin_console_tunnel` | Forwards the Keycloak admin console to `http://localhost:8080/admin` | |
| `app_dir` | Where the checkout lives on the host | |
| `spaces_bucket` | Name of the app's Spaces bucket | |
| `spaces_endpoint` | The Spaces S3 endpoint for the app's region | |
| `spaces_access_key_id` | Access key ID for the app's bucket-scoped Spaces key | sensitive |
| `spaces_secret_key` | Secret key for the app's bucket-scoped Spaces key | sensitive |

The last four go in the droplet's `.env` (`deploy/init-env.sh`, #42) as the app's own S3 configuration.

### Teardown

```bash
terraform destroy
```

This deletes the droplet, volume, reserved IP, firewall, project and DNS records. The app's Spaces bucket
(`digitalocean_spaces_bucket.app`) is **not** force-destroyed: if it still has objects in it, `terraform destroy`
fails on that resource rather than silently deleting users' datasets and models. Empty the bucket by hand first if
you really want it gone, or leave it (and its scoped key) in place and only destroy everything else by targeting:
`terraform destroy -target=digitalocean_droplet.host -target=digitalocean_volume.data -target=digitalocean_reserved_ip.host`.
