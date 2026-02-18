locals {
  instance_name = "${var.project_name}-${var.region}"
  full_labels   = merge(var.labels, { managed_by = "terraform" })
}
