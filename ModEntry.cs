using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;

namespace BusLocations
{
    /// <summary>
    /// Main entry point for the Bus Locations mod.
    /// This mod replaces the vanilla bus system with a customizable destination selector,
    /// allowing content packs to add new bus destinations.
    /// </summary>
    public class ModEntry : Mod
    {
        /*********
        ** Constants
        *********/
        // Ticket machine tile coordinates at the Bus Stop map.
        // The machine occupies a vertical column of tiles that players can interact with.
        private const int TicketMachineTileX = 17;
        private const int TicketMachineTileYTop = 10;
        private const int TicketMachineTileYBottom = 12;

        // Pam's designated driving position (where she stands when the bus is operational)
        private const int PamStandingTileX = 21;
        private const int PamStandingTileY = 10;

        // Brief pause when warping to make the transition feel less jarring
        private const int WarpFreezeDurationMs = 700;

        // The mail flag that indicates the player has completed the Vault bundle,
        // which canonically repairs the bus in vanilla Stardew Valley
        private const string VaultCompletedMailFlag = "ccVault";

        /*********
        ** Fields
        *********/
        /// <summary>User-configurable options loaded from config.json.</summary>
        private ModConfig Config = null!;

        /// <summary>All bus destinations loaded from content packs.</summary>
        private BusDestination[] Destinations = null!;

        /// <summary>
        /// Pre-built dialogue responses for the destination selection menu.
        /// Cached at startup for performance since destinations don't change at runtime.
        /// </summary>
        private Response[] DestinationChoices = null!;


        /*********
        ** Public methods
        *********/
        /// <summary>SMAPI calls this method once when the mod is first loaded.</summary>
        /// <param name="helper">Provides simplified APIs for writing mods.</param>
        public override void Entry(IModHelper helper)
        {
            this.Config = helper.ReadConfig<ModConfig>();

            // We need two event handlers to fully intercept the ticket machine:
            // 1. OnUpdateTicking: Continuously suppresses action buttons while hovering over
            //    the ticket machine. This prevents the vanilla bus code from ever seeing the input.
            // 2. OnButtonPressed: Handles the actual interaction when buttons are pressed.
            //    This is where we show our custom destination menu.
            helper.Events.GameLoop.UpdateTicking += this.OnUpdateTicking;
            helper.Events.Input.ButtonPressed += this.OnButtonPressed;

            this.LoadAllDestinations(helper);
            this.BuildDestinationMenu();
        }


        /*********
        ** Private methods - Initialization
        *********/
        /// <summary>
        /// Loads bus destinations from all available sources:
        /// 1. SMAPI content packs (other mods that depend on this one)
        /// 2. Nested folders within this mod's directory (for bundled destinations like Desert)
        /// </summary>
        private void LoadAllDestinations(IModHelper helper)
        {
            var destinations = new List<BusDestination>();

            // Load from SMAPI-registered content packs
            // These are separate mods that declare this mod as a dependency
            foreach (var pack in helper.ContentPacks.GetOwned())
            {
                this.Monitor.Log($"Reading content pack: {pack.Manifest.Name} {pack.Manifest.Version} from {pack.DirectoryPath}");

                try
                {
                    this.LoadDestinationsFromPack(
                        pack.ReadJsonFile<BusDestinationPack>("content.json"),
                        pack.ReadJsonFile<BusDestination>("content.json"),
                        destinations,
                        pack.Manifest.Name
                    );
                }
                catch (Exception ex)
                {
                    this.Monitor.Log($"Failed to load content pack '{pack.Manifest.Name}': {ex.Message}", LogLevel.Error);
                }
            }

            // Load from nested folders (e.g., "[BL] Desert" folder inside this mod)
            // This allows shipping default destinations without requiring separate content packs
            foreach (var directory in Directory.GetDirectories(helper.DirectoryPath))
            {
                var contentFilePath = Path.Combine(directory, "content.json");
                if (!File.Exists(contentFilePath))
                    continue;

                var folderName = Path.GetFileName(directory);
                this.Monitor.Log($"Reading nested content pack from {folderName}");
                var relativePath = Path.Combine(folderName, "content.json");

                try
                {
                    this.LoadDestinationsFromPack(
                        helper.Data.ReadJsonFile<BusDestinationPack>(relativePath),
                        helper.Data.ReadJsonFile<BusDestination>(relativePath),
                        destinations,
                        folderName
                    );
                }
                catch (Exception ex)
                {
                    this.Monitor.Log($"Failed to load nested content pack '{folderName}': {ex.Message}", LogLevel.Error);
                }
            }

            this.Destinations = destinations.ToArray();

            if (this.Destinations.Length == 0)
                this.Monitor.Log("No bus destinations were loaded. Make sure you have content packs installed or the [BL] Desert folder is present.", LogLevel.Warn);
            else
                this.Monitor.Log($"Loaded {this.Destinations.Length} bus destination(s)");
        }

        /// <summary>
        /// Parses a content.json file and adds valid destinations to the list.
        /// Supports two JSON formats for backwards compatibility:
        /// - New format: { "locations": [ {...}, {...} ] }  (multiple destinations)
        /// - Legacy format: { "mapname": "...", ... }       (single destination)
        /// </summary>
        /// <param name="pack">Parsed multi-destination format (may be null if JSON doesn't match).</param>
        /// <param name="single">Parsed single-destination format (may be null if JSON doesn't match).</param>
        /// <param name="destinations">The list to add successfully parsed destinations to.</param>
        /// <param name="sourceName">Name of the content pack source for logging purposes.</param>
        private void LoadDestinationsFromPack(BusDestinationPack? pack, BusDestination? single, List<BusDestination> destinations, string sourceName)
        {
            // Prefer the new multi-destination format
            if (pack?.Locations?.Count > 0)
            {
                foreach (var destination in pack.Locations)
                {
                    if (this.ValidateDestination(destination, sourceName))
                        destinations.Add(destination);
                }
                return;
            }

            // Fall back to legacy single-destination format
            // Check MapName to distinguish between an actual destination and a failed parse
            if (single != null && !string.IsNullOrEmpty(single.MapName))
            {
                if (this.ValidateDestination(single, sourceName))
                    destinations.Add(single);
                return;
            }

            // Neither format matched - log a warning
            this.Monitor.Log($"Could not parse content.json from '{sourceName}'. Expected 'locations' array or single destination with 'mapname'.", LogLevel.Warn);
        }

        /// <summary>
        /// Validates that a destination has all required fields populated.
        /// </summary>
        /// <param name="destination">The destination to validate.</param>
        /// <param name="sourceName">Name of the content pack source for logging purposes.</param>
        /// <returns>True if valid, false otherwise.</returns>
        private bool ValidateDestination(BusDestination destination, string sourceName)
        {
            var errors = new List<string>();

            if (string.IsNullOrWhiteSpace(destination.MapName))
                errors.Add("mapname is missing or empty");

            if (string.IsNullOrWhiteSpace(destination.DisplayName))
                errors.Add("displayname is missing or empty");

            if (errors.Count > 0)
            {
                this.Monitor.Log($"Invalid destination in '{sourceName}': {string.Join(", ", errors)}. Skipping.", LogLevel.Warn);
                return false;
            }

            return true;
        }

        /// <summary>
        /// Pre-builds the dialogue menu responses shown when interacting with the ticket machine.
        /// Each destination becomes a selectable option showing name and price.
        /// </summary>
        private void BuildDestinationMenu()
        {
            var choices = new List<Response>(this.Destinations.Length + 1);

            for (int i = 0; i < this.Destinations.Length; i++)
            {
                var dest = this.Destinations[i];
                // The response key is the array index, which we'll parse back in the handler
                choices.Add(new Response(
                    responseKey: i.ToString(),
                    responseText: $"{dest.DisplayName} ({dest.TicketPrice}g)"
                ));
            }

            choices.Add(new Response("Cancel", this.Helper.Translation.Get("menu.cancel")));
            this.DestinationChoices = choices.ToArray();
        }


        /*********
        ** Private methods - Event handlers
        *********/
        /// <summary>
        /// Runs every game tick (~60 times per second) to suppress vanilla bus interactions.
        ///
        /// WHY THIS IS NECESSARY:
        /// Stardew Valley's bus interaction is hardcoded into the BusStop location.
        /// We can't simply add our own interaction - we must prevent the vanilla code
        /// from running first. By suppressing action buttons BEFORE they're processed,
        /// we ensure the vanilla bus code never sees the player trying to interact.
        ///
        /// SPLIT-SCREEN NOTE:
        /// In split-screen mode, this event fires once per screen per tick. SMAPI automatically
        /// swaps game context (Game1.player, Game1.currentLocation, etc.) for each screen,
        /// so our checks naturally apply to the correct player.
        /// </summary>
        private void OnUpdateTicking(object? sender, UpdateTickingEventArgs e)
        {
            if (!this.CanInteractWithWorld())
                return;

            if (!this.IsPlayerAtTicketMachine())
                return;

            // Suppress both controller and mouse inputs to prevent vanilla interaction.
            // We check IsDown rather than waiting for press events because we need to
            // catch the input before it propagates to the game's interaction system.
            this.SuppressButtonIfHeld(SButton.ControllerA);
            this.SuppressButtonIfHeld(SButton.MouseRight);
        }

        /// <summary>
        /// Handles button press events to show our custom destination menu.
        /// This runs after OnUpdateTicking has already suppressed the vanilla interaction.
        /// </summary>
        private void OnButtonPressed(object? sender, ButtonPressedEventArgs e)
        {
            if (!this.CanInteractWithWorld())
                return;

            if (!this.IsActionButton(e))
                return;

            if (!this.IsPlayerAtTicketMachine(e.Cursor.GrabTile))
                return;

            // Suppress this specific button press so vanilla code doesn't also process it
            this.Helper.Input.Suppress(e.Button);

            this.ShowDestinationMenu();
        }

        /// <summary>
        /// Callback invoked when the player selects a destination from the menu.
        /// Validates the selection, checks requirements, and warps the player.
        /// </summary>
        /// <param name="who">The player who made the selection.</param>
        /// <param name="selectedKey">The response key - either a destination index or "Cancel".</param>
        private void OnDestinationSelected(Farmer who, string selectedKey)
        {
            if (selectedKey == "Cancel")
                return;

            // Defensive validation for the selection
            if (!int.TryParse(selectedKey, out int destinationIndex))
            {
                this.Monitor.Log($"Invalid destination selection key: {selectedKey}", LogLevel.Warn);
                return;
            }

            if (destinationIndex < 0 || destinationIndex >= this.Destinations.Length)
            {
                this.Monitor.Log($"Destination index out of range: {destinationIndex}", LogLevel.Warn);
                return;
            }

            var destination = this.Destinations[destinationIndex];

            if (!this.ValidateTravelRequirements(who, destination))
                return;

            this.WarpPlayerToDestination(who, destination);
        }


        /*********
        ** Private methods - Helpers
        *********/
        /// <summary>
        /// Checks if the game state allows world interactions for the current screen's player.
        /// We should not intercept inputs when the world isn't loaded, when menus/dialogues
        /// are open, or when the player is otherwise not free to act.
        /// </summary>
        /// <remarks>
        /// Context.IsPlayerFree is the recommended SMAPI check that accounts for:
        /// - World not ready
        /// - Active menus or dialogues
        /// - Cutscenes or events
        /// - Player is frozen or otherwise unable to act
        /// This works correctly in split-screen as SMAPI scopes Context to the current screen.
        /// </remarks>
        private bool CanInteractWithWorld()
        {
            // Context.IsPlayerFree handles all the common "can the player act?" checks
            if (!Context.IsPlayerFree)
                return false;

            // Additional null safety for the current location
            if (Game1.currentLocation == null)
                return false;

            return true;
        }

        /// <summary>Checks if a button is currently held and suppresses it if so.</summary>
        private void SuppressButtonIfHeld(SButton button)
        {
            if (this.Helper.Input.IsDown(button))
                this.Helper.Input.Suppress(button);
        }

        /// <summary>
        /// Determines if the pressed button is an "action" button (used for interacting with objects).
        /// Android has special handling because it uses tap-to-move mechanics.
        /// </summary>
        private bool IsActionButton(ButtonPressedEventArgs e)
        {
            if (Constants.TargetPlatform == GamePlatform.Android)
            {
                // On Android, a "tap on self" (cursor tile equals player tile) is an action
                return e.Button == SButton.MouseLeft && e.Cursor.GrabTile == e.Cursor.Tile;
            }

            return e.Button.IsActionButton();
        }

        /// <summary>Checks if the player is currently at the BusStop map and targeting the ticket machine.</summary>
        private bool IsPlayerAtTicketMachine()
        {
            return this.IsPlayerAtTicketMachine(Game1.currentCursorTile);
        }

        /// <summary>Checks if a given tile position is the ticket machine at the BusStop.</summary>
        /// <param name="cursorTile">The tile position to check.</param>
        private bool IsPlayerAtTicketMachine(Vector2 cursorTile)
        {
            // Must be at the bus stop location
            // Using Contains() handles both "BusStop" and any modded variants like "Custom_BusStop"
            if (!Game1.currentLocation.Name.Contains("BusStop"))
                return false;

            // Check if cursor is on the ticket machine's tile column
            bool isCorrectX = cursorTile.X == TicketMachineTileX;
            bool isCorrectY = cursorTile.Y >= TicketMachineTileYTop && cursorTile.Y <= TicketMachineTileYBottom;

            return isCorrectX && isCorrectY;
        }

        /// <summary>Shows the destination selection dialogue or an appropriate error message.</summary>
        private void ShowDestinationMenu()
        {
            // The Vault bundle completion unlocks the bus in vanilla Stardew Valley.
            // We check the host player's mail since they control world progression.
            bool busIsRepaired = Game1.MasterPlayer.mailReceived.Contains(VaultCompletedMailFlag);

            if (!busIsRepaired)
            {
                Game1.drawObjectDialogue(this.Helper.Translation.Get("message.out-of-service"));
                return;
            }

            if (this.Destinations.Length == 0)
            {
                Game1.drawObjectDialogue(this.Helper.Translation.Get("message.no-destinations"));
                return;
            }

            Game1.currentLocation.createQuestionDialogue(
                this.Helper.Translation.Get("menu.question"),
                this.DestinationChoices,
                this.OnDestinationSelected
            );
        }

        /// <summary>
        /// Validates that all requirements are met before allowing travel.
        /// Shows appropriate error messages if requirements aren't met.
        /// </summary>
        /// <param name="who">The player attempting to travel.</param>
        /// <param name="destination">The destination to validate against.</param>
        /// <returns>True if travel is allowed, false otherwise.</returns>
        private bool ValidateTravelRequirements(Farmer who, BusDestination destination)
        {
            // Check funds using the specific player who is traveling
            if (who.Money < destination.TicketPrice)
            {
                Game1.drawObjectDialogue(
                    Game1.content.LoadString("Strings\\Locations:BusStop_NotEnoughMoneyForTicket")
                );
                return false;
            }

            // Optionally require Pam to be at her driving position
            if (this.Config.RequirePam && !this.IsPamAtBusStop())
            {
                Game1.drawObjectDialogue(this.Helper.Translation.Get("message.no-driver"));
                return false;
            }

            return true;
        }

        /// <summary>Checks if Pam is present at the bus stop in her driving position.</summary>
        private bool IsPamAtBusStop()
        {
            NPC pam = Game1.getCharacterFromName("Pam");

            if (pam == null)
                return false;

            if (!Game1.currentLocation.characters.Contains(pam))
                return false;

            // Pam must be at her designated driving position
            return pam.Tile == new Vector2(PamStandingTileX, PamStandingTileY);
        }

        /// <summary>Warps the specified player to the selected destination after deducting the ticket cost.</summary>
        /// <param name="who">The player to warp.</param>
        /// <param name="destination">The destination to warp to.</param>
        private void WarpPlayerToDestination(Farmer who, BusDestination destination)
        {
            who.Money -= destination.TicketPrice;

            // Halt movement and briefly freeze to smooth the transition
            who.Halt();
            who.freezePause = WarpFreezeDurationMs;

            // Note: Game1.warpFarmer warps the local player (Game1.player), which should
            // match 'who' since the dialogue callback is triggered by the interacting player.
            // In split-screen, each player has their own Game1.player context.
            Game1.warpFarmer(
                destination.MapName,
                destination.DestinationX,
                destination.DestinationY,
                destination.ArrivalFacing
            );
        }
    }

    /*********
    ** Data Models
    *********/
    /// <summary>
    /// Represents a single bus destination that players can travel to.
    /// These are loaded from content.json files in content packs.
    /// </summary>
    /// <remarks>
    /// Property names use PascalCase in C# but are serialized as camelCase in JSON
    /// (e.g., "mapname", "displayname") for backwards compatibility with existing content packs.
    /// </remarks>
    internal class BusDestination
    {
        /// <summary>
        /// The internal map name used by Stardew Valley (e.g., "Desert", "Town").
        /// This must match an existing game location or a custom map added by another mod.
        /// </summary>
        public string MapName { get; set; } = string.Empty;

        /// <summary>
        /// The human-readable name shown in the destination selection menu (e.g., "Calico Desert").
        /// </summary>
        public string DisplayName { get; set; } = string.Empty;

        /// <summary>The X tile coordinate where the player will arrive on the destination map.</summary>
        public int DestinationX { get; set; }

        /// <summary>The Y tile coordinate where the player will arrive on the destination map.</summary>
        public int DestinationY { get; set; }

        /// <summary>
        /// The direction the player will face upon arrival.
        /// Uses Stardew Valley's facing constants: 0=Up, 1=Right, 2=Down, 3=Left.
        /// </summary>
        public int ArrivalFacing { get; set; }

        /// <summary>The cost in gold to travel to this destination.</summary>
        public int TicketPrice { get; set; }
    }

    /// <summary>
    /// Container for the multi-destination JSON format.
    /// Allows a single content pack to define multiple bus destinations.
    /// </summary>
    /// <example>
    /// JSON format:
    /// {
    ///   "locations": [
    ///     { "mapname": "Desert", "displayname": "Calico Desert", ... },
    ///     { "mapname": "Beach", "displayname": "The Beach", ... }
    ///   ]
    /// }
    /// </example>
    internal class BusDestinationPack
    {
        /// <summary>The list of bus destinations defined in this content pack.</summary>
        public List<BusDestination> Locations { get; set; } = new();
    }
}
