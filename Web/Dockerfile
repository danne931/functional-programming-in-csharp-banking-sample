FROM bitnami/dotnet-sdk:8

RUN mkdir -p /home/app

COPY ./bin/Release/net8.0/publish /home/app

WORKDIR /home/app

# Enable access to Petabridge.Cmd (pbm) to issue Akka system
# commands from CLI.
RUN dotnet new tool-manifest
RUN dotnet tool install pbm

CMD ["dotnet", "Web.dll"]
