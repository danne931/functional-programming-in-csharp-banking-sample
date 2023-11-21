FROM bitnami/dotnet-sdk

RUN mkdir -p /home/app

COPY ./bin/Debug/net7.0/publish /home/app

WORKDIR /home/app

# Enable access to Petabridge.Cmd (pbm) to issue Akka system
# commands from CLI.
RUN dotnet new tool-manifest
RUN dotnet tool install pbm

ENV DOTNET_URLS="http://+:3000"
ENV ASPNETCORE_ENVIRONMENT="Development"
ENV MockThirdPartyBank__Port="5007"
ENV ConnectionStrings__Postgres="postgres://postgres:password@postgres/akkabank"
ENV ConnectionStrings__PostgresAdoFormat="Server=postgres;Database=akkabank;Uid=postgres;Pwd=password"
ENV EmailServiceUri="https://api.useplunk.com/v1/"
ENV SerilogOutputFile="logs.json"
ENV AkkaSystemName="bank"
ENV AkkaRemoting__Port="8081"
ENV ClusterStartupMethod="Discovery"
ENV ClusterDiscoveryStartup__EndpointNames="account.1,account.2"
ENV AkkaPersistence__DbProvider="PostgreSQL.15"
ENV AkkaPersistence__JournalTableName="akka_event_journal"
ENV AkkaPersistence__SnapshotTableName="akka_snapshots"
ENV Quartz__SchedulerName="Quartz Bank Scheduler"
ENV Quartz__TablePrefix="qrtz_"

CMD ["dotnet", "Web.dll"]
