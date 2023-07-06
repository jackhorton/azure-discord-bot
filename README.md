# Roadmap

## v1 - Basic functionality

```sh
# List all servers, both online and offline
/azurebot server list

# Create a new server for the given game. A server is analogous to a save file. This implicitly starts the server as well.
/azurebot server new $NAME $GAME $REGION $SKU $TIMESPAN

# Starts the given server for the given duration, after which the server will turn off automatically.
/azurebot server start $NAME $TIMESPAN
```

## v2 - More control options

```sh
# Explicitly stop a server before the original $TIMESPAN is up
/azurebot server stop $NAME

# Add $TIMESPAN to the existing server lifetime
/azurebot server extend $NAME $TIMESPAN

# Dumps information about the server, including when it will expire
/azurebot server info $NAME

# Deletes a given server's resources
/azurebot server delete $NAME
```

## v3 - permissions
`/azurebot server edit <name> <option-key> <option-value>` (different servers allow changing different config options)

`/azurebot server login <name>` (sends login information to the user if they have permission)

## v4 - agent
- enlightened agent on the machine can perform server-specific actions
- turn off as soon as no players are connected



## Proposed SQL Schema
### Servers
Id, Name, ResourceId, Game, UserId, GuildId, CurrentState, CurrentSku

### ServerStateLog
Id, ServerId, Timestamp, NewState, NewSku



## Custom images
- create custom images using packer, stored in a shared image gallery
- one image per game (FactorioImage and MinecraftImage will differ)
- install the game server dont generate any worlds in the custom image
- start from public windows or ubuntu SKUs
