variable "cpu_alert" {
  type = object({
    enabled             = optional(bool, true)
    severity            = optional(number, 3)
    frequency           = optional(string, "PT5M")
    window_size         = optional(string, "PT15M")
    threshold           = optional(number, 80)
    operator            = optional(string, "GreaterThan")
  })
  description = "Configuration for CPU usage alert."
  default     = {}
}

variable "memory_alert" {
  type = object({
    enabled             = optional(bool, true)
    severity            = optional(number, 3)
    frequency           = optional(string, "PT5M")
    window_size         = optional(string, "PT15M")
    threshold           = optional(number, 85)
    operator            = optional(string, "GreaterThan")
    dimensions          = optional(list(object({
      name     = string
      operator = optional(string, "Include")
      values   = list(string)
    })), [])
  })
  description = "Configuration for memory usage alert."
  default     = {}
}

variable "action_group_ids" {
  type        = list(string)
  description = "List of action group IDs for alert notifications."
  default     = []
}
