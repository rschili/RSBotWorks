# Targets
.PHONY: build push

build:
	cd src && dotnet build

push: build test
	./build_and_push.sh

test:
	cd src/RSBotWorks.Tests/ && dotnet run --disable-logo --output Detailed

test-integration:
	cd src/RSBotWorks.Tests/ && dotnet run --treenode-filter /*/RSBotWorks.Tests.Integration/*/* --disable-logo --output Detailed

ide:
	code .