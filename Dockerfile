FROM bitnami/dotnet-sdk

RUN mkdir -p /home/app

COPY . /home/app

WORKDIR /home/app

# Restore tools defined in .config/dotnet-tools.json
# so can access Petabridge.Cmd (pbm) to issue Akka system
# commands from CLI.
RUN dotnet tool restore

WORKDIR /home/app/Web

CMD ["dotnet", "run"]
