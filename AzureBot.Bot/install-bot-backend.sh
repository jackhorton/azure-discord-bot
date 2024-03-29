#!/bin/bash

set -euo pipefail

KEY_VAULT_URL=$1
AZUREMONITOR_CONNECTIONSTRING=$2
CLIENT_ID=$3
TENANT_ID=$4
QUEUE_URL=$5
COSMOS_URL=$6

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

mkdir -p /var/www/azurebot
cp -r ./* /var/www/azurebot
chown --recursive www-data /var/www

# For some reason, PEM certificates are just not playing nice with me. Converting to PFX ahead
# of time solves the issue.
# TODO: Re-export and reload PFX when PEM gets updated
CERT_SOURCE=/var/lib/waagent/Microsoft.Azure.KeyVault.Store
CERT_DEST=/var/www
openssl pkcs12 -export \
    -out ${CERT_DEST}/acme-https-cert.pfx \
    -in ${CERT_SOURCE}/${KEY_VAULT_URL}.acme-https-cert \
    -inkey ${CERT_SOURCE}/${KEY_VAULT_URL}.acme-https-cert \
    -password pass:""
chown www-data ${CERT_DEST}/acme-https-cert.pfx
chmod u+rwx ${CERT_DEST}/acme-https-cert.pfx

tee /etc/systemd/system/azurebot.service > /dev/null <<EOM
[Unit]
Description=AzureBot backend API

[Service]
WorkingDirectory=/var/www/azurebot
ExecStart=dotnet /var/www/azurebot/AzureBot.Bot.dll
StandardOutput=syslog
StandardError=syslog
SyslogIdentifier=azurebot-bot
Restart=always
# Restart service after 10 seconds if the dotnet service crashes:
RestartSec=10
KillSignal=SIGINT
User=www-data
Environment=ASPNETCORE_ENVIRONMENT=Production
Environment=DOTNET_PRINT_TELEMETRY_MESSAGE=false
Environment=AZUREMONITOR__CONNECTIONSTRING=${AZUREMONITOR_CONNECTIONSTRING}
Environment=AZUREBOT__CLIENTID=${CLIENT_ID}
Environment=AZUREBOT__TENANTID=${TENANT_ID}
Environment=AZUREBOT__QUEUEURL=${QUEUE_URL}
Environment=AZUREBOT__COSMOSURL=${COSMOS_URL}
Environment=KESTREL__ENDPOINTS__HTTPS__URL=https://0.0.0.0:443
Environment=KESTREL__ENDPOINTS__HTTPS__CERTIFICATE__PATH=${CERT_DEST}/acme-https-cert.pfx

[Install]
WantedBy=multi-user.target
EOM

tee /etc/rsyslog.d/10-azurebot.conf > /dev/null <<EOM
if \$programname == 'azurebot-bot' then /var/log/azurebot/azurebot.bot.log
& stop
EOM

mkdir -p /var/log/azurebot
chown --recursive syslog:adm /var/log/azurebot

systemctl daemon-reload
systemctl restart rsyslog
systemctl restart azurebot