FROM bitnami/dotnet-sdk

RUN mkdir -p /home/app

COPY ./bin/Debug/net7.0/publish /home/app

WORKDIR /home/app

CMD ["dotnet", "MockThirdPartyBankTransferReceiver.Web.dll"]
