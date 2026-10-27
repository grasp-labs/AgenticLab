provider "aws" {
  region = var.region
}

terraform {
  required_version = ">= 1.5"

  required_providers {
    aws = {
      source  = "hashicorp/aws"
      version = "~> 6.0"
    }
  }

  backend "s3" {
    region = "eu-north-1"
    bucket = "grasptfstates"
    key    = "ds/agentic-lab/ecr/terraform.tfstate"
  }
}
