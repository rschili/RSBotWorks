# Targets
.PHONY: build test test-integration demo ide compose-up compose-down push

build:
	cd src && dotnet build

test:
	cd src/RSBotWorks.Tests/ && dotnet run --disable-logo --output Detailed

test-integration:
	cd src/RSBotWorks.Tests/ && dotnet run --treenode-filter /*/RSBotWorks.Tests.Integration/*/* --disable-logo --output Detailed

demo:
	cd src/SaneAI.Demo && dotnet run

ide:
	code .

# Local deployment with docker compose (or podman compose)
compose-up:
	docker compose up -d --build

compose-down:
	docker compose down

# Container images are built and published by GitHub Actions
# (see .github/workflows/docker-publish.yml) — no local push scripts needed.
push:
	@echo "Images are published via GitHub Actions (docker-publish workflow)."
	@echo "Trigger: push to main, tag v*, or run the workflow manually."
