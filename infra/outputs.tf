output "ip_address" {
  description = "The host's reserved IP."
  value       = digitalocean_reserved_ip.host.ip_address
}

output "app_url" {
  description = "Where users use the app."
  value       = "https://${local.app_host}"
}

output "auth_url" {
  description = "Keycloak's public URL (sign-in only; the admin console isn't served here)."
  value       = "https://${local.auth_host}"
}

output "ssh" {
  description = "SSH in as root."
  value       = "ssh root@${digitalocean_reserved_ip.host.ip_address}"
}

output "admin_console_tunnel" {
  description = "Forward the Keycloak admin console to http://localhost:8080/admin."
  value       = "ssh -N -L 8080:127.0.0.1:8080 root@${digitalocean_reserved_ip.host.ip_address}"
}

output "app_dir" {
  description = "Where the checkout and its root-only .env live on the host."
  value       = local.app_dir
}

output "spaces_bucket" {
  description = "Name of the app's Spaces bucket."
  value       = digitalocean_spaces_bucket.app.name
}

output "spaces_endpoint" {
  description = "Spaces S3 endpoint for the app's region."
  value       = "https://${var.region}.digitaloceanspaces.com"
}

output "spaces_access_key_id" {
  description = "Access key ID for the app's bucket-scoped Spaces key. Goes in the host's .env (see #42)."
  value       = digitalocean_spaces_key.app.access_key
  sensitive   = true
}

output "spaces_secret_key" {
  description = "Secret key for the app's bucket-scoped Spaces key. Goes in the host's .env (see #42)."
  value       = digitalocean_spaces_key.app.secret_key
  sensitive   = true
}
