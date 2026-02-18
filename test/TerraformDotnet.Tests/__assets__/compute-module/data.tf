data "cloud_image" "latest" {
  name   = "ubuntu-22.04"
  region = var.region
}

data "cloud_identity" "current" {
}
