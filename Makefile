# Targets
.PHONY: build push

build:
	cd src && dotnet build

push_wernstrom: build test
	./push_wernstrom.sh

push_stoll: build test
	./push_stoll.sh

push: push_wernstrom push_stoll

test:
	cd src/RSBotWorks.Tests/ && dotnet run --disable-logo --output Detailed

test-integration:
	cd src/RSBotWorks.Tests/ && dotnet run --treenode-filter /*/RSBotWorks.Tests.Integration/*/* --disable-logo --output Detailed

ide:
	code .