FROM bitnami/dotnet-sdk

RUN mkdir -p /home/app

COPY ./bin/Debug/net7.0/publish /home/app

WORKDIR /home/app

# Enable access to Petabridge.Cmd (pbm) to issue Akka system
# commands from CLI.
RUN dotnet new tool-manifest
RUN dotnet tool install pbm

ENV ASPNETCORE_ENVIRONMENT="Development"
ENV MockThirdPartyBank__Port="5007"
ENV ConnectionStrings__Postgres="postgres://postgres:password@postgres/akkabank"
ENV ConnectionStrings__PostgresAdoFormat="Server=postgres;Database=akkabank;Uid=postgres;Pwd=password"
ENV EmailServiceUri="https://api.useplunk.com/v1/"
ENV AkkaRemoting__Port="8082"
ENV ClusterStartupMethod="Discovery"
ENV ClusterDiscoveryStartup__EndpointNames="account.1,account.2"

CMD ["dotnet", "Account.Service.dll"]
