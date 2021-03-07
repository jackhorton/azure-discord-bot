#!/bin/bash

set -euo pipefail

export DEBIAN_FRONTEND=noninteractive

apt-get update -y
apt-get install -y debian-keyring debian-archive-keyring apt-transport-https
curl -1sLf 'https://dl.cloudsmith.io/public/caddy/stable/gpg.key' | apt-key add -
curl -1sLf 'https://dl.cloudsmith.io/public/caddy/stable/debian.deb.txt' | tee /etc/apt/sources.list.d/caddy-stable.list
apt-get update -y
apt-get upgrade -y
apt-get install caddy -y

tee /etc/caddy/Caddyfile > /dev/null <<EOM
:80

reverse_proxy 127.0.0.1:5000
EOM
systemctl restart caddy

systemctl is-active --quiet azurebot && systemctl stop azurebot

mkdir -p /var/www/azurebot
cp ./AzureBot.Bot ./AzureBot.Bot.pdb ./appsettings.json /var/www/azurebot
chown --recursive www-data /var/www/azurebot

tee /etc/systemd/system/azurebot.service > /dev/null <<EOM
[Unit]
Description=AzureBot backend API

[Service]
WorkingDirectory=/var/www/azurebot
ExecStart=/var/www/azurebot/AzureBot.Bot
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