resource "cloud_private_endpoint" "main" {
  count = var.enable_private_endpoint ? 1 : 0

  name        = "${local.instance_name}-pe"
  instance_id = cloud_instance.main[0].id
  subnet_id   = cloud_subnet.main[0].id
}

resource "cloud_vnet" "main" {
  name       = "${var.project_name}-vnet"
  cidr_block = var.network_cidr
  region     = var.region
}

resource "cloud_subnet" "main" {
  count = var.enable_private_endpoint ? 1 : 0

  name       = "${var.project_name}-subnet"
  vnet_id    = cloud_vnet.main.id
  cidr_block = var.subnet_prefixes[0]
}
