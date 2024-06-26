using Discord;
using Discord.WebSocket;

namespace Sudachi
{
    public static class Log
    {
        public static Task LogAsync(LogMessage msg)
        {
            var cc = Console.ForegroundColor;
            Console.ForegroundColor = msg.Severity switch
            {
                LogSeverity.Critical => ConsoleColor.DarkRed,
                LogSeverity.Error => ConsoleColor.Red,
                LogSeverity.Warning => ConsoleColor.DarkYellow,
                LogSeverity.Info => ConsoleColor.White,
                LogSeverity.Verbose => ConsoleColor.Green,
                LogSeverity.Debug => ConsoleColor.DarkGreen,
                _ => throw new NotImplementedException("Invalid log level " + msg.Severity)
            };
            Console.Out.WriteLineAsync(msg.ToString());
            Console.ForegroundColor = cc;
            return Task.CompletedTask;
        }

        public static async Task LogErrorAsync(Exception e, SocketSlashCommand ctx)
        {
            await LogAsync(new LogMessage(LogSeverity.Error, e.Source, e.Message, e));

            if (ctx != null)
            {
                try
                {
                    var embed = new EmbedBuilder
                    {
                        Color = Color.Red,
                        Title = $"{e.GetType()}",
                        Description = $"{e.Message}"
                    }.Build();
                    await ctx.FollowupAsync(embed: embed);
                }
                catch (Exception ex)
                {
                    await LogAsync(new LogMessage(LogSeverity.Critical, ex.Source, ex.Message, ex));
                }
            }
        }
    }
}