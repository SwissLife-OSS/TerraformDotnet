output "cluster_id" {
  value       = db_cluster.main.id
  description = "The ID of the database cluster."
}

output "cluster_endpoint" {
  value       = db_cluster.main.endpoint
  description = "The primary endpoint for the database cluster."
  sensitive   = true
  depends_on  = [db_cluster.main]
}
