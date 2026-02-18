resource "db_cluster" "main" {
  name             = var.db_name
  engine_version   = var.db_version
  admin_password   = var.db_password
  backup_retention = var.backup_retention

  maintenance_window {
    day  = var.maintenance_window.day
    hour = var.maintenance_window.hour
  }
}

resource "db_replica" "replicas" {
  for_each = var.replica_configs

  name       = each.value.name
  cluster_id = db_cluster.main.id
  region     = each.value.region
  priority   = each.value.priority
  readonly   = each.value.readonly
}
