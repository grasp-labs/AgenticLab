resource "aws_ecr_repository" "component" {
  for_each = local.components

  name                 = "${local.app}-${each.key}"
  image_tag_mutability = "MUTABLE" # allows re-pushing 'latest'

  image_scanning_configuration {
    scan_on_push = true
  }

  tags = merge(local.tags, { Component = each.key })
}

resource "aws_ecr_lifecycle_policy" "component" {
  for_each   = aws_ecr_repository.component
  repository = each.value.name

  policy = jsonencode({
    rules = [
      {
        rulePriority = 1
        description  = "Expire untagged images after 7 days"
        selection = {
          tagStatus   = "untagged"
          countType   = "sinceImagePushed"
          countUnit   = "days"
          countNumber = 7
        }
        action = { type = "expire" }
      },
      {
        rulePriority = 2
        description  = "Keep the last 20 images"
        selection = {
          tagStatus   = "any"
          countType   = "imageCountMoreThan"
          countNumber = 20
        }
        action = { type = "expire" }
      }
    ]
  })
}

output "ecr_repository_urls" {
  description = "ECR repository URL per component"
  value       = { for name, repo in aws_ecr_repository.component : name => repo.repository_url }
}
