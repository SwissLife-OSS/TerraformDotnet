variable "network_cidr" {
  type        = string
  description = "The CIDR block for the virtual network."
  default     = "10.0.0.0/16"
}

variable "subnet_prefixes" {
  type        = list(string)
  description = "List of subnet prefixes."
  default     = ["10.0.1.0/24"]
}

variable "enable_private_endpoint" {
  type        = bool
  description = "Whether to enable a private endpoint."
  default     = false
}
