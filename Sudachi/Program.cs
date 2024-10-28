using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.DependencyInjection;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;
using Sudachi.Data;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Color = Discord.Color;
using Image = SixLabors.ImageSharp.Image;

namespace Sudachi
{
    public sealed class Program
    {
        private IServiceProvider _serviceProvider;

        private DiscordSocketClient _client;

        public static async Task Main()
        {
            try
            {
                await new Program().StartAsync();
            }
            catch (FileNotFoundException) // This probably means a dll is missing
            {
                throw;
            }
            catch (Exception e)
            {
                if (!Debugger.IsAttached)
                {
                    if (!Directory.Exists("Logs"))
                        Directory.CreateDirectory("Logs");
                    File.WriteAllText("Logs/Crash-" + DateTime.Now.ToString("yyyy-MM-dd-HH-mm-ss-ff") + ".txt", e.ToString());
                }
                else // If an exception occur, the program exit and is relaunched
                    throw;
            }
        }

        private bool AreFileSameContent(string path1, string path2)
            => File.Exists(path1) && File.Exists(path2) && File.ReadAllBytes(path1).SequenceEqual(File.ReadAllBytes(path2));

        private bool AreFolderSameContent(string path1, string path2)
        {
            var files1 = Directory.GetFiles(path1);
            var files2 = Directory.GetFiles(path2);
            if (files1.Length != files2.Length)
            {
                return false;
            }

            for (int i = 0; i < files1.Length; i++)
            {
                if (!AreFileSameContent(files1[i], files2[i])) return false;
            }
            return true;
        }

        /// <summary>
        /// Resize a single image to Katsis comic format and put it at the right position
        /// </summary>
        /// <param name="sourcePath">Input image path</param>
        /// <param name="targetPath">Output image path</param>
        private void ResizeImage(string sourcePath, string targetPath)
        {
            using Image img = Image.Load(sourcePath);

            var w = img.Width;
            var h = img.Height;
            var ratio = w > h ? (w / 400f) : (h / 500f);

            img.Mutate(x => x
            .Resize((int)(img.Width / ratio), (int)(img.Height / ratio))
            );

            img.Save(targetPath);
        }

        /// <summary>
        /// Resize all images in a folder and put them in the output dir
        /// </summary>
        /// <param name="sourcePath">Folder containing all images</param>
        /// <param name="targetPath">Output folder</param>
        private void ResizeContent(string sourcePath, string targetPath)
        {
            foreach (var f in Directory.GetFiles(sourcePath))
            {
                var name = new FileInfo(f).Name;
                ResizeImage(f, $"{targetPath}/{name}");
            }
        }

        /// <summary>
        /// Copy all files in folder
        /// </summary>
        /// <remarks>Not recursive</remarks>
        /// <param name="sourcePath">Input folder</param>
        /// <param name="targetPath">Output folder</param>
        private void CopyContent(string sourcePath, string targetPath)
        {
            foreach (var f in Directory.GetFiles(sourcePath))
            {
                var name = new FileInfo(f).Name;
                File.Copy(f, $"{targetPath}/{name}", true);
            }
        }

        /// <summary>
        /// Delete all files and folders in path
        /// </summary>
        private void ClearFolder(string path)
        {
            foreach (var f in Directory.GetFiles(path))
            {
                File.Delete(f);
            }
            foreach (var d in Directory.GetDirectories(path))
            {
                ClearFolder(d);
            }
        }

        /// <summary>
        /// Clean main Temp/ folder where git stuff are cloned
        /// </summary>
        private void CleanTempFolder()
        {
            // Delete temp stuff
            if (Directory.Exists("Temp"))
            {
                if (Directory.Exists("Temp/Comics/.git"))
                {
                    var directory = new DirectoryInfo("Temp/Comics/.git")
                    {
                        Attributes = FileAttributes.Normal
                    };
                    foreach (var info in directory.GetFileSystemInfos("*", SearchOption.AllDirectories))
                    {
                        info.Attributes = FileAttributes.Normal;
                    }

                    directory.Delete(true);
                }

                Directory.Delete("Temp", true);
            }
            Directory.CreateDirectory("Temp");
        }

        public async Task StartAsync()
        {
            _client = new(new()
            {
                LogLevel = LogSeverity.Info,
                GatewayIntents = GatewayIntents.Guilds | GatewayIntents.GuildMessages | GatewayIntents.DirectMessages
            });
            _client.Log += Log.LogAsync;

            CleanTempFolder();

            await Log.LogAsync(new LogMessage(LogSeverity.Info, "Setup", "Initialising bot"));

            // Load credentials
            if (!File.Exists("Keys/credentials.json"))
                throw new FileNotFoundException("Missing Credentials file");
            var credentials = JsonSerializer.Deserialize<Credentials>(File.ReadAllText("Keys/credentials.json"))!;

            _serviceProvider = new ServiceCollection()
                .AddSingleton(_client)
                .AddSingleton(credentials)
                .AddSingleton(new ProjectContainer() { Projects = JsonSerializer.Deserialize<Project[]>(File.ReadAllText($"{credentials.WebsiteDataBasePath}/projects.json")) })
                .AddSingleton<Random>()
                .AddSingleton<HttpClient>()
                .BuildServiceProvider();

            _client.Ready += Ready;
            _client.SlashCommandExecuted += SlashCommandExecuted;

            await _client.LoginAsync(TokenType.Bot, credentials.BotToken);
            await _client.StartAsync();

            // We keep the bot online
            await Task.Delay(-1);
        }

        private bool _isUpdatingComics = false;
        private bool _isUploadingGallery = false;

        private string ToSentenceCase(string str)
            => char.ToUpperInvariant(str[0]) + str[1..].ToLowerInvariant();

        private async Task SlashCommandExecuted(SocketSlashCommand arg)
        {
            var cmd = arg.CommandName.ToUpperInvariant();

            if (cmd == "PING")
            {
                var content = $"Pong";
                var now = DateTime.Now;
                await arg.RespondAsync(content);
                var time = DateTime.Now - now;
                await arg.ModifyOriginalResponseAsync(x => x.Content = $"{content} {time.TotalMilliseconds:n2}ms");
            }
            else if (cmd == "HEADPAT")
            {
                if (DateTime.Now.Hour < 10 || DateTime.Now.Hour > 21)
                {
                    await arg.RespondAsync("💤");
                }
                else if (arg.User.Id % 81 == 0)
                {
                    await arg.RespondAsync("💖"); // Lucky you
                }
                else
                {
                    await arg.RespondAsync("❤️");
                }
            }
            else if (cmd == "UPLOAD")
            {
                var artists = new Dictionary<ulong, string>()
                {
                    { 298907835251687424, "fractal" },
                    { 454328717171490816, "pauline" },
                    { 144851584478740481, "zirk" },
                    { 1132610174289461278, "dekakumadon" },
                    { 169575908267524096, "sweaterweather" },
                    { 188117682606964737, "nehneh" }
                };
                if (!artists.ContainsKey(arg.User.Id))
                {
                    await arg.RespondAsync(embed: new EmbedBuilder()
                    {
                        Color = Color.Red,
                        Title = "Insufficient permissions",
                        Description = "You are not allowed to do this, if you believe this is a mistake, please contact Zirk"
                    }.Build());
                }
                else if (_isUploadingGallery)
                {
                    await arg.RespondAsync(embed: new EmbedBuilder()
                    {
                        Color = Color.Red,
                        Title = "Invalid operation",
                        Description = "A gallery upload is already ongoing"
                    }.Build(), ephemeral: true);
                }
                else
                {
                    _isUploadingGallery = true;
                    var path = (IAttachment)arg.Data.Options.First(x => x.Name == "image").Value;
                    var names = (string?)arg.Data.Options.FirstOrDefault(x => x.Name == "names")?.Value;
                    if (names != null)
                    {

                    }

                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await arg.RespondAsync("Downloading file...");

                            using var resp = await _serviceProvider.GetService<HttpClient>().GetAsync(path.Url);
                            var format = path.Url.Split(".").Last().Split('?')[0];
                            var target = $"file";
                            using var fs = File.Create($"{target}.{format}");
                            await resp.Content.CopyToAsync(fs);
                            fs.Dispose();

                            await arg.ModifyOriginalResponseAsync(x => x.Content = "Updating data...");
                            var data = JsonSerializer.Deserialize<List<ImageData>>(File.ReadAllText($"{_serviceProvider.GetService<Credentials>().GalleryDataBasePath}/info.json"));
                            var uid = Guid.NewGuid().ToString();
                            var curr = new ImageData()
                            {
                                Format = format,
                                Id = uid,
                                Author = artists[arg.User.Id],
                                Rating = (int)(long)arg.Data.Options.First(x => x.Name == "rating").Value,
                                IsCanon = (bool?)arg.Data.Options.FirstOrDefault(x => x.Name == "canon")?.Value,
                                Comment = (string?)arg.Data.Options.FirstOrDefault(x => x.Name == "comment")?.Value,
                                Tags = new()
                                {
                                    Characters = names == null ? [] : names.Split(',').Select(x => x.ToLowerInvariant().Trim()).ToArray(),
                                    Parodies = [],
                                    Others = []
                                }
                            };
                            data.Add(curr);
                            var newJson = JsonSerializer.Serialize(data);
                            File.WriteAllText($"{_serviceProvider.GetService<Credentials>().GalleryDataBasePath}/info.json", newJson);

                            using var bmp = SixLabors.ImageSharp.Image.Load($"{target}.{format}");
                            var w = bmp.Width;
                            var h = bmp.Height;
                            var ratio = w > h ? (w / 200f) : (h / 300f);
                            bmp.Mutate(x => x.Resize((int)(w / ratio), (int)(h / ratio)));
                            bmp.Save($"{_serviceProvider.GetService<Credentials>().GalleryDataBasePath}/thumbnails/{uid}.{format}");

                            File.Copy($"{target}.{format}", $"{_serviceProvider.GetService<Credentials>().GalleryDataBasePath}/images/{uid}.{format}");

                            await arg.ModifyOriginalResponseAsync(x => x.Content = "Updating tags...");
                            Dictionary<string, ImageTagData> tagData;
                            if (File.Exists($"{_serviceProvider.GetService<Credentials>().GalleryDataBasePath}/tags.json")) tagData = JsonSerializer.Deserialize<Dictionary<string, ImageTagData>>(File.ReadAllText($"{_serviceProvider.GetService<Credentials>().GalleryDataBasePath}/tags.json"));
                            else tagData = new();

                            foreach (var f in tagData)
                            {
                                tagData[f.Key].Images = f.Value.Images.Distinct().ToList();
                            }

                            var tags = new string[][] {
                                    [ $"author_{curr.Author}" ],
                                    [],
                                    curr.Tags.Characters == null ? [] : curr.Tags.Characters.Select(x => $"name_{x.ToLowerInvariant()}").ToArray(),
                                    []
                                };
                            List<string> tagsStr = new();
                            foreach (var t in tags)
                            {
                                if (t == null) continue;
                                foreach (var t2 in t)
                                {
                                    if (tagData.ContainsKey(t2))
                                    {
                                        if (!tagData[t2].Images.Contains(curr.Id))
                                        {
                                            tagData[t2].Images.Add(curr.Id);
                                        }
                                    }
                                    else
                                    {
                                        tagData.Add(t2, new()
                                        {
                                            Images = [curr.Id],
                                            Definition = string.Empty
                                        });
                                    }
                                }
                            }
                            File.WriteAllText($"{_serviceProvider.GetService<Credentials>().GalleryDataBasePath}/tags.json", JsonSerializer.Serialize(tagData));

                            await arg.ModifyOriginalResponseAsync(x => x.Content = "Data updated with success!");
                        }
                        catch (Exception ex)
                        {
                            await arg.ModifyOriginalResponseAsync(x => x.Content = $"Error: {ex.Message}");
                            await Log.LogAsync(new(LogSeverity.Error, ex.Source, ex.Message, ex));
                        }
                        finally
                        {
                            _isUploadingGallery = false;
                        }
                    });
                }
            }
            else if (cmd == "UPDATE")
            {
                if (arg.User.Id != 144851584478740481 && arg.User.Id != 298907835251687424)
                {
                    await arg.RespondAsync(embed: new EmbedBuilder()
                    {
                        Color = Color.Red,
                        Title = "Insufficient permissions",
                        Description = "You are not allowed to do this"
                    }.Build(), ephemeral: true);
                }
                else if (_isUpdatingComics)
                {
                    await arg.RespondAsync(embed: new EmbedBuilder()
                    {
                        Color = Color.Red,
                        Title = "Invalid operation",
                        Description = "A comic update is already ongoing"
                    }.Build(), ephemeral: true);
                }
                else
                {
                    _isUpdatingComics = true;
                    _ = Task.Run(async () =>
                    {
                        var process = new StringBuilder();
                        try
                        {
                            CleanTempFolder();

                            process.AppendLine("```");
                            process.Append("Cloning repo...");
                            await arg.RespondAsync(process.ToString() + "\n```");

                            var p = new Process()
                            {
                                StartInfo = new()
                                {
                                    FileName = "git",
                                    Arguments = $"clone --recurse-submodules https://oauth2:{_serviceProvider.GetService<Credentials>().GithubToken}@github.com/KatsisStudio/Comics",
                                    WorkingDirectory = "Temp/"
                                }
                            };
                            p.Start();
                            p.WaitForExit();

                            if (p.ExitCode != 0)
                            {
                                await Log.LogErrorAsync(new InvalidOperationException($"Clone ended with {p.ExitCode} exit code"), arg);
                                return;
                            }

                            process.AppendLine(" OK");
                            process.AppendLine();

                            process.AppendLine("Updating git config...");
                            Process.Start(new ProcessStartInfo() { FileName = "git", Arguments = "config user.name \"Sudachi\"", WorkingDirectory = "Temp/Comics" });
                            Process.Start(new ProcessStartInfo() { FileName = "git", Arguments = "config --unset user.email", WorkingDirectory = "Temp/Comics" });
                            process.AppendLine(" OK");
                            process.AppendLine();
                            process.AppendLine("Comics founds:");
                            foreach (var folder in Directory.GetDirectories("Temp/Comics/comics/"))
                            {
                                var name = JsonSerializer.Deserialize<ComicData>(File.ReadAllText($"{folder}/info.json")).Name;
                                process.AppendLine(name);
                                p = Process.Start(new ProcessStartInfo() { FileName = "git", Arguments = "pull origin main", WorkingDirectory = folder });
                                p.WaitForExit();
                                if (p.ExitCode != 0)
                                {
                                    throw new InvalidOperationException($"Pull ended with {p.ExitCode} exit code for {name}");
                                }
                            }
                            process.AppendLine();
                            process.Append("Updating repo...");
                            await arg.ModifyOriginalResponseAsync(x => x.Content = process.ToString() + "\n```");

                            p = Process.Start(new ProcessStartInfo() { FileName = "git", Arguments = "add --all", WorkingDirectory = "Temp/Comics" });
                            p.WaitForExit();
                            if (p.ExitCode != 0)
                            {
                                throw new InvalidOperationException($"Add ended with {p.ExitCode} exit code");
                            }

                            p = new Process()
                            {
                                StartInfo = new ProcessStartInfo()
                                {
                                    FileName = "git",
                                    Arguments = "status -s",
                                    WorkingDirectory = "Temp/Comics",
                                    UseShellExecute = false,
                                    RedirectStandardOutput = true
                                }
                            };
                            p.Start();
                            var stdout = p.StandardOutput.ReadToEnd();

                            if (string.IsNullOrEmpty(stdout))
                            {
                                process.AppendLine(" OK");
                                process.AppendLine("Nothing to commit");
                            }
                            else
                            {
                                process.AppendLine(" OK");
                                process.AppendLine("Changes:");
                                process.AppendLine(stdout);
                                process.AppendLine();

                                p = Process.Start(new ProcessStartInfo() { FileName = "git", Arguments = "commit -m \"Update submodules\"", WorkingDirectory = "Temp/Comics" });
                                p.WaitForExit();
                                if (p.ExitCode != 0)
                                {
                                    throw new InvalidOperationException($"Commit ended with {p.ExitCode} exit code");
                                }

                                /*process.Append("Pushing changes...");
                                await arg.ModifyOriginalResponseAsync(x => x.Content = process.ToString() + "\n```");

                                p = Process.Start(new ProcessStartInfo() { FileName = "git", Arguments = "push origin main", WorkingDirectory = "Temp/Comics" });
                                p.WaitForExit();
                                if (p.ExitCode != 0)
                                {
                                    throw new InvalidOperationException($"Push ended with {p.ExitCode} exit code");
                                }

                                process.AppendLine(" OK");*/
                            }

                            process.AppendLine();

                            process.AppendLine("Updating comics...");
                            await arg.ModifyOriginalResponseAsync(x => x.Content = process.ToString() + "\n```");
                            foreach (var folder in Directory.GetDirectories("Temp/Comics/comics/"))
                            {
                                var data = JsonSerializer.Deserialize<ComicData>(File.ReadAllText($"{folder}/info.json"));
                                var name = data.Name;
                                process.Append($"{name}...");

                                var info = new FileInfo(folder);
                                var folderName = info.Name;
                                var remotePath = _serviceProvider.GetService<Credentials>().ComicBasePath;

                                // All files are the same, no update needed
                                if (AreFileSameContent($"{folder}/info.json", $"{remotePath}{folderName}/info.json")
                                    && AreFolderSameContent($"{folder}/pages", $"{remotePath}{folderName}/pages")
                                    && AreFolderSameContent($"{folder}/assets", $"{remotePath}{folderName}/assets"))
                                {
                                    process.AppendLine($" Skip");
                                    await arg.ModifyOriginalResponseAsync(x => x.Content = process.ToString() + "\n```");
                                    continue;
                                }

                                await arg.ModifyOriginalResponseAsync(x => x.Content = process.ToString() + "\n```");
                                if (!Directory.Exists($"{remotePath}{folderName}")) Directory.CreateDirectory($"{remotePath}{folderName}");

                                // Copy and resized all images
                                if (Directory.Exists($"{remotePath}{folderName}/assets")) ClearFolder($"{remotePath}{folderName}/assets");
                                else Directory.CreateDirectory($"{remotePath}{folderName}/assets");
                                CopyContent($"{folder}/assets", $"{remotePath}{folderName}/assets");
                                if (Directory.Exists($"{remotePath}{folderName}/pages")) ClearFolder($"{remotePath}{folderName}/pages");
                                else Directory.CreateDirectory($"{remotePath}{folderName}/pages");
                                CopyContent($"{folder}/pages", $"{remotePath}{folderName}/pages");
                                if (Directory.Exists($"{remotePath}{folderName}/previews")) ClearFolder($"{remotePath}{folderName}/previews");
                                else Directory.CreateDirectory($"{remotePath}{folderName}/previews");
                                ResizeContent($"{folder}/pages", $"{remotePath}{folderName}/previews");
                                File.Copy($"{folder}/info.json", $"{remotePath}{folderName}/info.json", true);

                                // Resize the thumbnail
                                var thumbnailPathIn = $"{folder}/assets/{data.Preview}";
                                var thumbnailFI = new FileInfo(thumbnailPathIn);
                                ResizeImage(thumbnailPathIn, $"{remotePath}{folderName}/previews/thumbnail{thumbnailFI.Extension}");
                                process.AppendLine($" OK");
                            }
                            await arg.ModifyOriginalResponseAsync(x => x.Content = process.ToString() + "\n```");

                            process.AppendLine();
                            process.AppendLine("All operations completed successfully");
                            await arg.ModifyOriginalResponseAsync(x => x.Content = process.ToString() + "\n```");
                        }
                        catch (Exception e)
                        {
                            process.AppendLine(" ERROR");
                            process.AppendLine(e.Message);
                            process.AppendLine();
                            process.AppendLine("A fatal error occurred, operation failed");
                            await arg.ModifyOriginalResponseAsync(x => x.Content = process.ToString() + "\n```");
                            Console.WriteLine(e);
                        }
                        finally
                        {
                            _isUpdatingComics = false;
                        }
                    });
                }
            }
        }

        private const ulong DebugGuildId = 1169565317920456705;
        private async Task Ready()
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    var cmds = new SlashCommandBuilder[]
                    {
                   new()
                   {
                       Name = "ping",
                       Description = "Ping the bot"
                   },
                   new()
                   {
                       Name = "update",
                       Description = "Update comics"
                   },
                   new SlashCommandBuilder()
                   .WithName("upload")
                   .WithDescription("Upload an image to the gallery")
                   .AddOptions(
                       new SlashCommandOptionBuilder()
                        .WithName("image")
                        .WithDescription("Drawing to upload")
                        .WithType(ApplicationCommandOptionType.Attachment)
                        .WithRequired(true),
                       new SlashCommandOptionBuilder()
                       .WithName("rating")
                       .WithDescription("How NSFW is the image")
                       .WithType(ApplicationCommandOptionType.Integer)
                       .AddChoice("Safe", 0)
                       .AddChoice("Questionnable", 1)
                       .AddChoice("Explicit", 1)
                        .WithRequired(true),
                       new SlashCommandOptionBuilder()
                        .WithName("names")
                        .WithDescription("Character names (coma separated)")
                        .WithType(ApplicationCommandOptionType.String)
                        .WithRequired(false),
                       new SlashCommandOptionBuilder()
                        .WithName("canon")
                        .WithDescription("Is the drawing canon in the lore of your world")
                        .WithType(ApplicationCommandOptionType.Boolean)
                        .WithRequired(false),
                       new SlashCommandOptionBuilder()
                        .WithName("comment")
                        .WithDescription("Additional optional comment by the artist")
                        .WithType(ApplicationCommandOptionType.String)
                        .WithRequired(false)
                        ),
                   new()
                   {
                       Name = "headpat",
                       Description = "Attempt to headpat Sudachi"
                   },
                    }.Select(x => x.Build()).ToArray();
                    foreach (var cmd in cmds)
                    {
                        if (Debugger.IsAttached)
                        {
                            await _client.GetGuild(DebugGuildId).CreateApplicationCommandAsync(cmd);
                        }
                        else
                        {
                            await _client.CreateGlobalApplicationCommandAsync(cmd);
                        }
                    }
                    if (Debugger.IsAttached)
                    {
                        await _client.GetGuild(DebugGuildId).BulkOverwriteApplicationCommandAsync(cmds);
                    }
                    else
                    {
                        await _client.GetGuild(DebugGuildId).DeleteApplicationCommandsAsync();
                        await _client.BulkOverwriteGlobalApplicationCommandsAsync(cmds);
                    }
                    await Log.LogAsync(new LogMessage(LogSeverity.Info, "Setup", "Command initialized"));
                }
                catch (Exception ex)
                {
                    await Log.LogErrorAsync(ex, null);
                }
            });
        }
    }
}