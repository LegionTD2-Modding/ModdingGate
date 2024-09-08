all: clean build

clean:
	@dotnet clean ModsGate.csproj

build:
	@dotnet build ModsGate.csproj

release: clean
	@dotnet build ModsGate.csproj --configuration Release
