FROM bitnami/dotnet-sdk:8

RUN mkdir -p /home/app

COPY ./bin/Release/net8.0/publish /home/app

WORKDIR /home/app

CMD ["dotnet", "MockThirdPartyBankTransferReceiver.Web.dll"]
