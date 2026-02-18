terraform {
  required_version = ">= 1.3.0"

  required_providers {
    db = {
      source  = "registry.example.com/example/db"
      version = "~> 4.0"
    }
  }
}
