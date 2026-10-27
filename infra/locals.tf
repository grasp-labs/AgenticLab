locals {
  domain       = "ds"
  service_name = "agentic-lab"
  app          = "${local.domain}-${local.service_name}" # ds-agentic-lab

  # One repository per Dockerfile target; the Makefile pushes to ds-agentic-lab-<component>.
  components = toset(["web", "aiservice", "mcpserver"])

  tags = {
    Domain      = local.domain
    ServiceName = local.service_name
    App         = local.app
    ManagedBy   = "Terraform"
  }
}
