output "instance_id" {
  value       = cloud_instance.main.id
  description = "The ID of the compute instance."
}

output "instance_ip" {
  value       = cloud_instance.main.public_ip
  description = "The public IP address of the instance."
}

output "private_endpoint_ip" {
  value       = var.enable_private_endpoint ? cloud_private_endpoint.main[0].ip_address : null
  description = "The private endpoint IP address, if enabled."
}

output "connection_string" {
  value       = cloud_instance.main.connection_string
  description = "The connection string for the instance."
  sensitive   = true
}
