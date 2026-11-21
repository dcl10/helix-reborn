terraform {
  required_version = ">= 1.17"

  required_providers {
    digitalocean = {
      source  = "digitalocean/digitalocean"
      version = "~> 2.107"
    }
  }

  # State lives in a DO Spaces bucket (S3-compatible), created by hand once (see docs/deployment.md). Configure
  # with -backend-config=backend.hcl (see backend.hcl.example); never commit backend.hcl.
  backend "s3" {}
}

# Your own token, from the DIGITALOCEAN_TOKEN environment variable. Not the app's token, which only goes in the
# host's .env.
provider "digitalocean" {}
