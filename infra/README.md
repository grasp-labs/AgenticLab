# Infrastructure

Terraform for the container registries the sandbox Kubernetes deployment pulls from: one ECR
repository per deployed Dockerfile target (`ds-agentic-lab-web`, `-aiservice`, `-mcpserver`)
in account 999125116186, `eu-north-1`. State lives in `s3://grasptfstates/ds/agentic-lab/ecr/`.

From the repository root, with AWS credentials for that account:

```sh
make provision   # terraform init + apply
make deploy      # build linux/amd64 images and push them, tagged with the git commit and latest
```

The Kubernetes manifest is `apps/sandbox/ds-agentic-lab/ds-application.yaml` in
`grasp-labs/ds-application-manifests`; pin its image tags to the tag `make deploy` prints.
