## v1 - basic functionality
`/azurebot server list`

`/azurebot server start <name> <timespan>`

`/azurebot server stop <name>`

## v2 - custom server images with steamcmd
`/azurebot server new <name> <game> <region> <allowed-role>`

`/azurebot server delete <name>`

`/azurebot server info <name>`

## v3 - permissions
`/azurebot server edit <name> <option-key> <option-value>` (different servers allow changing different config options)

`/azurebot server login <name>` (sends login information to the user if they have permission)

## v4 - agent
- enlightened agent on the machine can perform server-specific actions
- turn off as soon as no players are connected




### Servers
Id, Name, ResourceId, Game, UserId, GuildId, CurrentState, CurrentSku

### ServerStateLog
Id, ServerId, Timestamp, NewState, NewSku



### Custom images
- create custom images using packer, stored in a shared image gallery
- one image per game (FactorioImage and MinecraftImage will differ)
- install the game server dont generate any worlds in the custom image
- start from public windows or ubuntu SKUs