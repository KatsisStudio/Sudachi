using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.DependencyInjection;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;
using Sudachi.Data;
using System.Diagnostics;
using System.IO.Compression;
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
            else if (cmd == "PROJECT")
            {
                var allData = JsonSerializer.Deserialize<ProjectContainer>(await _serviceProvider.GetService<HttpClient>().GetStringAsync("https://katsis.net/?json=1")).Projects;
                var data = allData[_serviceProvider.GetService<Random>().Next(allData.Length)];

                var embed = new EmbedBuilder()
                {
                    Title = data.Name,
                    Description = data.Description,
                    Color = new Color(98, 17, 37),
                    ImageUrl = $"https://katsis.net/data/projects/{data.BaseFolder}/{data.Preview}",
                    Url = data.Links[0].Content,
                    Footer = new()
                    {
                        Text = $"Made by {string.Join(", ", data.Members)}"
                    }
                };
                var type = string.IsNullOrEmpty(data.GameGenre) ? data.Type : data.GameGenre;
                embed.AddField("Type", char.ToUpperInvariant(type[0]) + type[1..]);
                if (data.ContentWarnings.Any())
                {
                    embed.AddField("Tags", string.Join(", ", data.ContentWarnings));
                }
                await arg.RespondAsync(embed: embed.Build());
            }
            else if (cmd == "IMAGE")
            {
                var allData = JsonSerializer.Deserialize<ImageData[]>(await _serviceProvider.GetService<HttpClient>().GetStringAsync("https://gallery.katsis.net/?json=1"));
                var img = allData[_serviceProvider.GetService<Random>().Next(allData.Length)];

                var metadata = JsonSerializer.Deserialize<ImageData>(await _serviceProvider.GetService<HttpClient>().GetStringAsync($"https://gallery.katsis.net/i/{img.Id}?json=1"));

                await arg.RespondAsync(embed: new EmbedBuilder()
                {
                    Title = (metadata.TagsCleaned.Names.Any() ? $" {string.Join(", ", metadata.TagsCleaned.Names.Select(x => ToSentenceCase(x.Name[5..])))} by" : $"By") + $" {string.Join(", ", metadata.TagsCleaned.Authors.Select(x => ToSentenceCase(x.Name[7..])))}",
                    Url = $"https://gallery.katsis.net/?id={img.Id}",
                    Color = new Color(98, 17, 37),
                    ImageUrl = $"https://gallery.katsis.net/data/images/{img.Id}.{img.Format}"
                }.Build());
            }
            else if (cmd == "COMIC")
            {
                var allData = JsonSerializer.Deserialize<ComicContainer[]>(await _serviceProvider.GetService<HttpClient>().GetStringAsync("https://comic.katsis.net/?json=1"));
                var comic = allData[_serviceProvider.GetService<Random>().Next(allData.Length)];
                var data = comic.Metadata;
                var embed = new EmbedBuilder()
                {
                    Title = data.Name,
                    Description = data.Description,
                    Color = new Color(98, 17, 37),
                    ImageUrl = $"https://comic.katsis.net/{data.Preview}",
                    Url = $"https://comic.katsis.net/?comic={comic.Id}",
                    Footer = new()
                    {
                        Text = $"Made by {string.Join(", ", data.Members)}"
                    }
                };
                if (data.ContentWarnings.Any())
                {
                    embed.AddField("Tags", string.Join(", ", data.ContentWarnings));
                }
                await arg.RespondAsync(embed: embed.Build());
            }
            else if (cmd == "UPLOAD")
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
                    var path = (IAttachment)arg.Data.Options.First(x => x.Name == "zip").Value;

                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await arg.RespondAsync("Downloading file...");

                            {
                                using var resp = await _serviceProvider.GetService<HttpClient>().GetAsync(path.Url);
                                using var fs = File.Create("TmpGallery.zip");
                                await resp.Content.CopyToAsync(fs);
                            }

                            await arg.ModifyOriginalResponseAsync(x => x.Content = "Unzipping file...");
                            if (Directory.Exists("TmpGallery/"))
                            {
                                Directory.Delete("TmpGallery/", true);
                            }
                            ZipFile.ExtractToDirectory("TmpGallery.zip", "TmpGallery/");

                            await arg.ModifyOriginalResponseAsync(x => x.Content = "Updating data...");
                            var oldData = JsonSerializer.Deserialize<List<ImageData>>(File.ReadAllText($"{_serviceProvider.GetService<Credentials>().GalleryDataBasePath}/info.json"));
                            var newData = JsonSerializer.Deserialize<ImageData[]>(File.ReadAllText("TmpGallery/info.json"));

                            oldData.AddRange(newData);
                            oldData = oldData.DistinctBy(x => x.Id).ToList();
                            var newJson = JsonSerializer.Serialize(oldData);
                            File.WriteAllText($"{_serviceProvider.GetService<Credentials>().GalleryDataBasePath}/info.json", newJson);

                            CopyContent("TmpGallery/images", $"{_serviceProvider.GetService<Credentials>().GalleryDataBasePath}/images");
                            CopyContent("TmpGallery/thumbnails", $"{_serviceProvider.GetService<Credentials>().GalleryDataBasePath}/thumbnails");

                            await arg.ModifyOriginalResponseAsync(x => x.Content = "Updating tags...");
                            Dictionary<string, ImageTagData> tagData;
                            if (File.Exists($"{_serviceProvider.GetService<Credentials>().GalleryDataBasePath}/tags.json")) tagData = JsonSerializer.Deserialize<Dictionary<string, ImageTagData>>(File.ReadAllText($"{_serviceProvider.GetService<Credentials>().GalleryDataBasePath}/tags.json"));
                            else tagData = new();

                            foreach (var f in tagData)
                            {
                                tagData[f.Key].Images = f.Value.Images.Distinct().ToList();
                            }

                            foreach (var data in oldData)
                            {
                                var tags = new string[][] {
                                    [ $"author_{data.Author}" ],
                                    data.Tags.Parodies,
                                    data.Tags.Characters.Select(x => $"name_{x.ToLowerInvariant()}").ToArray(),
                                    data.Tags.Others
                                };
                                List<string> tagsStr = new();
                                foreach (var t in tags)
                                {
                                    foreach (var t2 in t)
                                    {
                                        if (tagData.ContainsKey(t2))
                                        {
                                            if (!tagData[t2].Images.Contains(data.Id))
                                            {
                                                tagData[t2].Images.Add(data.Id);
                                            }
                                        }
                                        else
                                        {
                                            tagData.Add(t2, new()
                                            {
                                                Images = [data.Id],
                                                Definition = string.Empty
                                            });
                                        }
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

                                process.Append("Pushing changes...");
                                await arg.ModifyOriginalResponseAsync(x => x.Content = process.ToString() + "\n```");

                                p = Process.Start(new ProcessStartInfo() { FileName = "git", Arguments = "push origin main", WorkingDirectory = "Temp/Comics" });
                                p.WaitForExit();
                                if (p.ExitCode != 0)
                                {
                                    throw new InvalidOperationException($"Push ended with {p.ExitCode} exit code");
                                }

                                process.AppendLine(" OK");
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
                   new()
                   {
                       Name = "upload",
                       Description = "Upload new images for gallery",

                       Options = new()
                       {
                           new()
                           {
                               Name = "zip",
                               Description = "ZIP files generated by images classifier software",
                               Type = ApplicationCommandOptionType.Attachment,
                               IsRequired = true
                           }
                       }
                   },
                   new()
                   {
                       Name = "image",
                       Description = "Get a random image by a Katsis member"
                   },
                   new()
                   {
                       Name = "headpat",
                       Description = "Attempt to headpat Sudachi"
                   },
                   new()
                   {
                       Name = "project",
                       Description = "Get information about a Katsis project"
                   },
                   new()
                   {
                       Name = "comic",
                       Description = "Get a random Katsis comic"
                   }
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
            });
        }
    }
}