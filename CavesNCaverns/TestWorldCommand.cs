using Vintagestory.API.Common;
using Vintagestory.API.Server;
using CavesAndCaverns.Config;

namespace CavesAndCaverns.Commands
{
    public class TestWorldCommand : ServerCommandBase
    {
        public TestWorldCommand() : base("wgen testworld", "Force generate air-only world for debugging", Privilege.controlserver) { }

        public override void Execute(IServerPlayer player, int groupId, CmdArgs args)
        {
            var config = CavesAndCavernsCore.ConfigManager.Config;
            config.DebugInverseWorld = true;
            config.DebugGlassSurfaceRivers = true;
            config.DebugGlassUndergroundRivers = true;
            config.DebugGlassLavaRivers = true;
            config.DebugGlassBedrock = true;
            CavesAndCavernsCore.ConfigManager.Save();

            player.SendMessage(groupId, "Test world mode enabled: Air world with glass rivers and bedrock.", EnumChatType.CommandSuccess);
        }

        public void Register(ICoreServerAPI sapi)
        {
            sapi.ChatCommands.Create(command)
                .WithDescription(description)
                .RequiresPrivilege(privilege)
                .HandleWith(args =>
                {
                    var player = args.Caller.Player as IServerPlayer;
                    if (player == null)
                        return TextCommandResult.Error("Command must be run by a player.");

                    Execute(player, player.PlayerUID.GetHashCode(), args.RawArgs);

                    // Fixed Line 24: Use args directly for ExecuteUnparsed
                    string regenCommand = "/wgen regen 1";
                    sapi.ChatCommands.ExecuteUnparsed(regenCommand, args, result =>
                    {
                        if (result.Status != EnumCommandStatus.Success)
                            sapi.Logger.Error("[CavesAndCaverns] Failed to execute /wgen regen: {0}", result.StatusMessage);
                    });

                    return TextCommandResult.Success();
                });
        }
    }

    public abstract class ServerCommandBase
    {
        protected readonly string command;
        protected readonly string description;
        protected readonly string privilege;

        protected ServerCommandBase(string command, string description, string privilege)
        {
            this.command = command;
            this.description = description;
            this.privilege = privilege;
        }

        public abstract void Execute(IServerPlayer player, int groupId, CmdArgs args);
    }
}