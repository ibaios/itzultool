.PHONY: publish

publish:
	dotnet publish /p:PublishProfile=runtime-linux
	dotnet publish /p:PublishProfile=runtime-win
	dotnet publish /p:PublishProfile=sdk-linux
	dotnet publish /p:PublishProfile=sdk-win
