variable "db_name" {
  type        = string
  description = "The name of the database cluster."
}

variable "db_password" {
  type        = string
  description = "The admin password for the database."
  sensitive   = true
}

variable "db_version" {
  type        = string
  description = "The database engine version."
  default     = "15"
}

variable "replica_configs" {
  type = set(object({
    name     = string
    region   = string
    priority = optional(number, 10)
    readonly = optional(bool, true)
  }))
  description = "Set of replica configurations."
  default     = []
}

variable "maintenance_window" {
  type = object({
    day  = string
    hour = number
  })
  description = "The preferred maintenance window."
  default = {
    day  = "sunday"
    hour = 3
  }
}

variable "backup_retention" {
  type        = number
  description = "Number of days to retain backups."
  default     = 7

  validation {
    condition     = var.backup_retention >= 1 && var.backup_retention <= 35
    error_message = "Backup retention must be between 1 and 35 days."
  }
}

variable "db_nullable_field" {
  type        = string
  description = "A nullable field for testing."
  default     = null
  nullable    = true
}
