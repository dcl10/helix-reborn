# The app's bucket for datasets, plots and models (see docs/architecture.md "Storage layout"). Created with the
# account's Spaces keys, which the provider reads from SPACES_ACCESS_KEY_ID and SPACES_SECRET_ACCESS_KEY, in
# addition to DIGITALOCEAN_TOKEN.
resource "digitalocean_spaces_bucket" "app" {
  name   = var.spaces_bucket_name
  region = var.region

  # Private: the browser never talks to Spaces directly, only the API and workers do, so no CORS configuration is
  # needed either.
  acl = "private"

  # Not managed by terraform destroy: tearing down the host must not delete users' datasets and models.
  force_destroy = false
}

# A key scoped to just this bucket, for the app's .env (see #42). Not the DIGITALOCEAN_TOKEN used by this Terraform.
resource "digitalocean_spaces_key" "app" {
  name = var.spaces_bucket_name

  grant {
    bucket     = digitalocean_spaces_bucket.app.name
    permission = "readwrite"
  }
}
