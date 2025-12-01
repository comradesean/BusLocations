# Bus Locations

A Stardew Valley mod that replaces the vanilla bus system with a customizable destination selector. Add new bus destinations through simple JSON content packs.

## Features

- **Multiple Destinations**: Choose from a list of destinations when using the ticket machine
- **Content Pack Support**: Add new destinations without editing the mod
- **Multi-Destination Packs**: Define multiple destinations in a single content pack
- **Configurable**: Optional setting to require Pam at the bus stop
- **Controller Support**: Works with both keyboard/mouse and controller inputs

## Installation

1. Install [SMAPI](https://smapi.io/)
2. Download this mod and extract to your `Stardew Valley/Mods` folder
3. Run the game

## Configuration

After running the game once, a `config.json` file will be created:

```json
{
  "RequirePam": false
}
```

| Option | Default | Description |
|--------|---------|-------------|
| `RequirePam` | `false` | When `true`, Pam must be at her driving position for the bus to operate |

## Creating Content Packs

Content packs allow you to add new bus destinations. Create a folder in your `Mods` directory with two files:

### manifest.json

```json
{
  "Name": "Your Pack Name",
  "Author": "Your Name",
  "Version": "1.0.0",
  "Description": "Adds new bus destinations",
  "UniqueID": "YourName.YourPackName",
  "ContentPackFor": {
    "UniqueID": "comradesean.BusLocations"
  }
}
```

### content.json

#### Single Destination

```json
{
  "mapname": "Beach",
  "displayname": "The Beach",
  "destinationX": 20,
  "destinationY": 4,
  "arrivalFacing": 2,
  "ticketPrice": 100
}
```

#### Multiple Destinations

```json
{
  "locations": [
    {
      "mapname": "Beach",
      "displayname": "The Beach",
      "destinationX": 20,
      "destinationY": 4,
      "arrivalFacing": 2,
      "ticketPrice": 100
    },
    {
      "mapname": "Mountain",
      "displayname": "The Mountain",
      "destinationX": 15,
      "destinationY": 7,
      "arrivalFacing": 2,
      "ticketPrice": 150
    }
  ]
}
```

### Destination Properties

| Property | Type | Description |
|----------|------|-------------|
| `mapname` | string | Internal map name (e.g., "Desert", "Beach", "Town") |
| `displayname` | string | Name shown in the destination menu |
| `destinationX` | int | X tile coordinate for player arrival |
| `destinationY` | int | Y tile coordinate for player arrival |
| `arrivalFacing` | int | Direction player faces on arrival: 0=Up, 1=Right, 2=Down, 3=Left |
| `ticketPrice` | int | Cost in gold |

## Requirements

- Stardew Valley 1.6+
- SMAPI 4.0+
- Vault bundle must be completed (bus repaired) for the ticket machine to work

## Compatibility

- Works in singleplayer and multiplayer
- Compatible with modded maps that contain "BusStop" in their name

## License

[MIT License](LICENSE)
