#!/bin/bash

set -euo pipefail

KEY_VAULT_URL=$1
AZUREMONITOR__CONNECTIONSSTRING=$2

curl -Lo /tmp/packages-microsoft-prod.deb https://packages.microsoft.com/config/ubuntu/20.04/packages-microsoft-prod.deb
dpkg -i /tmp/packages-microsoft-prod.deb
rm /tmp/packages-microsoft-prod.deb

export DEBIAN_FRONTEND=noninteractive
apt-get update -y
apt-get install -y apt-transport-https
apt-get upgrade -y
apt-get install -y aspnetcore-runtime-6.0

# Allow `dotnet` to bind to privileged port numbers like 443
setcap 'cap_net_bind_service=+ep' /usr/share/dotnet/dotnet

systemctl is-active --quiet azurebot && systemctl stop azurebot && systemctl disable azurebot

mkdir -p /var/www/azurebot
cp -r ./* /var/www/azurebot
chown --recursive www-data /var/www

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
Environment=AZUREMONITOR__CONNECTIONSSTRING=$AZUREMONITOR__CONNECTIONSSTRING
Environment=KESTREL__ENDPOINTS__HTTPS__URL=https://0.0.0.0:443
Environment=KESTREL__ENDPOINTS__HTTPS__CERTIFICATE__PATH=/var/www/${KEY_VAULT_URL}.acme-https-cert
Environment=KESTREL__ENDPOINTS__HTTPS__CERTIFICATE__KEYPATH=/var/www/${KEY_VAULT_URL}.acme-https-cert

[Install]
WantedBy=multi-user.target
EOM

systemctl enable azurebot
systemctl start azurebot