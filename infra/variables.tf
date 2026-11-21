variable "name" {
  description = "Prefix for every resource this creates (droplet, volume, firewall, project, tag)."
  type        = string
  default     = "helix"

  validation {
    condition     = can(regex("^[a-z][a-z0-9-]{1,40}$", var.name))
    error_message = "Use 2 to 41 lowercase letters, digits and hyphens, starting with a letter."
  }
}

variable "region" {
  description = "DigitalOcean region for the host, its volume, reserved IP and Spaces bucket."
  type        = string
  default     = "lon1"
}

variable "droplet_size" {
  description = "Host size. Training is CPU-heavy, so this defaults bigger than a typical Compose host."
  type        = string
  default     = "s-4vcpu-8gb"
}

variable "data_volume_size_gb" {
  description = "Size of the block-storage volume holding Docker's data (databases, the checkout, images)."
  type        = number
  default     = 50

  validation {
    condition     = var.data_volume_size_gb >= 10
    error_message = "Use at least 10 GB: the images alone take several."
  }
}

variable "ssh_public_key" {
  description = "Public key allowed to SSH in as root, such as the contents of ~/.ssh/id_ed25519.pub."
  type        = string
}

variable "ssh_allowed_cidrs" {
  description = "Addresses allowed to SSH in, such as [\"203.0.113.7/32\"] for your IP. Never 0.0.0.0/0."
  type        = list(string)

  validation {
    condition     = length(var.ssh_allowed_cidrs) > 0 && alltrue([for c in var.ssh_allowed_cidrs : can(cidrnetmask(c)) && !startswith(c, "0.0.0.0/")])
    error_message = "List one or more IPv4 CIDRs, such as 203.0.113.7/32; SSH can't be open to everyone."
  }
}

variable "domain" {
  description = "A domain hosted in this DigitalOcean account, for app.<domain> and auth.<domain>. Empty uses sslip.io hostnames from the reserved IP."
  type        = string
  default     = ""
}

variable "repo_url" {
  description = "Git repository the host clones and deploys from."
  type        = string
  default     = "https://github.com/dcl10/helix-reborn.git"
}

variable "repo_ref" {
  description = "Branch or tag the host checks out on first boot."
  type        = string
  default     = "main"
}

variable "spaces_bucket_name" {
  description = "Name of the app's DO Spaces bucket (datasets, plots and models). Must be globally unique across all of DigitalOcean."
  type        = string

  validation {
    condition     = can(regex("^[a-z0-9][a-z0-9-]*[a-z0-9]$", var.spaces_bucket_name)) && length(var.spaces_bucket_name) >= 3 && length(var.spaces_bucket_name) <= 63
    error_message = "spaces_bucket_name must be 3 to 63 lowercase letters, digits and hyphens, starting and ending with a letter or digit."
  }
}
