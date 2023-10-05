FROM bitnami/dotnet-sdk

RUN mkdir -p /home/app

COPY ./AkkaIntegrated /home/app

COPY .config /home/app

WORKDIR /home/app

RUN dotnet tool restore

CMD ["dotnet", "run"]
