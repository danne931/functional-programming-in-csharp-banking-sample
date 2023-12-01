FROM bitnami/dotnet-sdk:7

RUN mkdir -p /home/app

COPY ./bin/Debug/net7.0/publish /home/app

WORKDIR /home/app

# Enable access to Petabridge.Cmd (pbm) to issue Akka system
# commands from CLI.
RUN dotnet new tool-manifest
RUN dotnet tool install pbm

CMD ["dotnet", "Account.Service.dll"]
