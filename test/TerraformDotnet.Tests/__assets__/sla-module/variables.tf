terraform {
  required_version = ">= 1.9.0"
}

locals {
  allowed_regions = ["westeurope", "switzerlandnorth"]
}

variable "sla" {
  type        = string
  description = "The service level agreement."

  validation {
    condition     = contains(["silver", "gold"], var.sla)
    error_message = "sla must be silver or gold."
  }
}

variable "region" {
  type    = string
  default = "westeurope"

  validation {
    condition     = var.region == null || contains(local.allowed_regions, var.region)
    error_message = "region must be one of the allowed regions."
  }
}

variable "replicas" {
  type    = number
  default = 1

  validation {
    condition     = var.replicas >= 1 && var.replicas <= 10
    error_message = "replicas must be between 1 and 10."
  }

  validation {
    condition     = var.sla != "gold" || var.replicas >= 3
    error_message = "gold requires at least 3 replicas."
  }
}

variable "backup_vault_id" {
  type    = string
  default = null

  validation {
    condition     = var.sla != "gold" || var.backup_vault_id != null
    error_message = "backup_vault_id is required for the gold SLA."
  }
}

variable "cost_center" {
  type    = string
  default = "none"
}
