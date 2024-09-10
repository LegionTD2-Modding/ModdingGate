all: clean build

clean:
	@dotnet clean ModsGate.csproj

build:
	@dotnet build ModsGate.csproj

release: clean
	@dotnet build ModsGate.csproj --configuration Release

install: release
	@cp bin/Release/netstandard2.1/ModsGate.dll ~/.steam/steam/steamapps/common/Legion\ TD\ 2/BepInEx/plugins/ModsGate.dll

.PHONY: clean install