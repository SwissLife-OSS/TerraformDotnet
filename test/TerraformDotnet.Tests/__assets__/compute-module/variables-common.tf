variable "project_name" {
  type        = string
  description = "The name of the project."
}

variable "region" {
  type        = string
  description = "The deployment region."
}

variable "owner" {
  type        = string
  description = "The owner of the infrastructure."
}

variable "labels" {
  type        = map(string)
  description = "Labels to apply to all resources."
}

variable "instance_count" {
  type        = number
  description = "Number of compute instances to create."
  default     = 1
}

variable "enable_logging" {
  type        = bool
  description = "Whether to enable logging."
  default     = true
}

variable "allowed_cidrs" {
  type        = list(string)
  description = "List of allowed CIDR blocks."
  default     = []
}

variable "tier" {
  type        = string
  description = "The service tier. Valid options are basic, standard, and premium."
  default     = "standard"
}
