FROM bitnami/dotnet-sdk

RUN mkdir -p /home/app

COPY ./AkkaIntegrated /home/app

WORKDIR /home/app

CMD ["dotnet", "run"]
