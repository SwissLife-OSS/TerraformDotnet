module "main" {
    source = "../../child_module"

    name   = local.instance_name
    region = var.region
    tier   = var.tier
    count  = var.instance_count

    labels       = var.labels
    owner        = ""
    project_name = ""
}

module "data" {
    source = "../../child_module"

    name        = "${local.instance_name}-data"
    instance_id = cloud_instance.main[0].id
    size_gb     = 100

    depends_on = [cloud_instance.main]
}

module "allow" {
  source = "../../child_module"

  for_each = toset(var.allowed_cidrs)

  instance_id = cloud_instance.main[0].id
  cidr_block  = each.value
  action      = "allow"
  provider    = cloud.west
}
