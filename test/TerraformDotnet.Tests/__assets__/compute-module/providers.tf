terraform {
  required_version = ">= 1.5.0"

  required_providers {
    cloud = {
      source  = "registry.example.com/example/cloud"
      version = ">= 2.0"
    }
  }
}
