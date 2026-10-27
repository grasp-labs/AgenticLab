# ECR Configuration
AWS_REGION = eu-north-1
AWS_ACCOUNT_ID = 999125116186
ECR_REGISTRY = $(AWS_ACCOUNT_ID).dkr.ecr.$(AWS_REGION).amazonaws.com
COMPONENTS = web aiservice mcpserver
VERSION = $(shell git rev-parse --short HEAD)

.PHONY: provision deploy

# Provision the ECR repositories (ds-agentic-lab-<component>) using Terraform
provision:
	cd infra && terraform init -input=false && terraform apply

# Build linux/amd64 images and push them to ECR, tagged with the commit and latest
deploy:
	@test -z "$$(git status --porcelain)" || (echo "❌ Commit your changes first so the image tag matches a commit." && exit 1)
	@aws ecr get-login-password --region $(AWS_REGION) | docker login --username AWS --password-stdin $(ECR_REGISTRY)
	@for c in $(COMPONENTS); do \
		docker buildx build --platform linux/amd64 --provenance=false --target $$c \
			-t $(ECR_REGISTRY)/ds-agentic-lab-$$c:$(VERSION) \
			-t $(ECR_REGISTRY)/ds-agentic-lab-$$c:latest \
			--push . || exit 1; \
	done
	@echo "✅ Pushed ds-agentic-lab-{$(shell echo $(COMPONENTS) | tr ' ' ',')}:$(VERSION)"
