terraform {
  required_version = ">= 1.9.0"
}

locals {
  allowed_lock_kinds = ["CanNotDelete", "ReadOnly"]
  name_max_length    = 63
  guid_pattern       = "^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$"
  prefixed_name      = "${var.name_prefix}-${local.suffix}"
  suffix             = "app"
  workspace_name     = terraform.workspace
}

variable "name_prefix" {
  type    = string
  default = "lk"
}

variable "name" {
  type = string

  validation {
    condition     = length(var.name) >= 3 && length(var.name) <= local.name_max_length
    error_message = "The name must be between 3 and ${local.name_max_length} characters."
  }

  validation {
    condition     = can(regex("^[a-z0-9][a-z0-9-]*[a-z0-9]$", var.name))
    error_message = "The name may only contain lowercase letters, numbers and hyphens."
  }

  validation {
    condition     = !startswith(var.name, local.prefixed_name)
    error_message = "The name must not start with ${local.prefixed_name}."
  }
}

variable "lock" {
  type = object({
    kind = string
    name = optional(string, null)
  })
  default  = null
  nullable = true

  validation {
    condition     = var.lock != null ? contains(local.allowed_lock_kinds, var.lock.kind) : true
    error_message = "Lock kind must be either \"CanNotDelete\" or \"ReadOnly\"."
  }
}

variable "cidrs" {
  type    = list(string)
  default = []

  validation {
    condition     = alltrue([for c in var.cidrs : can(cidrhost(c, 0))])
    error_message = "All entries must be valid CIDR blocks: ${join(", ", var.cidrs)}"
  }
}

variable "retention_days" {
  type    = number
  default = null

  validation {
    condition     = var.retention_days == null ? true : var.retention_days >= 1 && var.retention_days <= 365
    error_message = format("retention_days must be between %d and %d, got %v.", 1, 365, var.retention_days)
  }
}

variable "principal_ids" {
  type    = set(string)
  default = []

  validation {
    condition     = alltrue([for id in var.principal_ids : can(regex(local.guid_pattern, id))])
    error_message = "principal_ids must be GUIDs."
  }
}

variable "tags" {
  type    = map(string)
  default = {}

  validation {
    condition     = alltrue([for k, v in var.tags : length(k) <= 512 && length(v) <= 256])
    error_message = "Tag keys may have up to 512 and values up to 256 characters."
  }
}

variable "network_acl" {
  type = object({
    default_action = optional(string, "Deny")
    ip_rules       = optional(list(string), [])
  })
  default = {}

  validation {
    condition     = contains(["Allow", "Deny"], var.network_acl.default_action)
    error_message = "default_action must be Allow or Deny."
  }

  validation {
    condition     = alltrue([for ip in var.network_acl.ip_rules : can(cidrhost(ip, 0))])
    error_message = "ip_rules must be CIDR blocks."
  }
}

variable "resource_id" {
  type    = string
  default = null

  validation {
    condition     = var.resource_id == null || provider::azapi::parse_resource_id("Microsoft.Storage/storageAccounts", var.resource_id) != null
    error_message = <<-EOT
      resource_id must be a storage account resource ID.
    EOT
  }

  validation {
    condition     = var.resource_id == null || data.azurerm_client_config.current.tenant_id != ""
    error_message = "resource_id must belong to the current tenant."
  }

  validation {
    condition     = var.resource_id == null || local.workspace_name != "forbidden"
    error_message = "resource_id is not allowed in this workspace."
  }
}
