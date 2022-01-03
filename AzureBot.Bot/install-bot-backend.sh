#!/bin/bash

set -euo pipefail

curl -Lo /tmp/packages-microsoft-prod.deb https://packages.microsoft.com/config/ubuntu/20.04/packages-microsoft-prod.deb
dpkg -i /tmp/packages-microsoft-prod.deb
rm /tmp/packages-microsoft-prod.deb

export DEBIAN_FRONTEND=noninteractive
apt-get update -y
apt-get install -y apt-transport-https
apt-get upgrade -y
apt-get install -y aspnetcore-runtime-6.0

systemctl is-active --quiet azurebot && systemctl stop azurebot

mkdir -p /var/www/azurebot
cp -r ./* /var/www/azurebot
chown --recursive www-data /var/www/azurebot

tee /etc/systemd/system/azurebot.service > /dev/null <<EOM
[Unit]
Description=AzureBot backend API

[Service]
WorkingDirectory=/var/www/azurebot
ExecStart=dotnet /var/www/azurebot/AzureBot.Bot.dll
Restart=always
# Restart service after 10 seconds if the dotnet service crashes:
RestartSec=10
KillSignal=SIGINT
SyslogIdentifier=azurebot-bot
User=www-data
Environment=ASPNETCORE_ENVIRONMENT=Production
Environment=DOTNET_PRINT_TELEMETRY_MESSAGE=false

[Install]
WantedBy=multi-user.target
EOM

systemctl start azurebot
systemctl enable azurebot